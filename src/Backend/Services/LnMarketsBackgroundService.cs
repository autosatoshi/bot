using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using Microsoft.Extensions.Options;

namespace AutoBot.Services;

public class LnMarketsBackgroundService(IServiceScopeFactory _scopeFactory, ILogger<LnMarketsBackgroundService> _logger, IOptions<LnMarketsOptions> _options) : BackgroundService
{
    private static class Constants
    {
        public const int SatoshisPerBitcoin = 100_000_000;
        public const string JsonRpcVersion = "2.0";
        public const string SubscribeMethod = "v1/public/subscribe";
        public const string FuturesChannel = "futures:btc_usd:last-price";
    }

    private readonly Uri _serverUri = new("wss://api.lnmarkets.com");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var webSocket = new ClientWebSocket();

            try
            {
                await webSocket.ConnectAsync(_serverUri, stoppingToken);

                var payload = $"{{\"jsonrpc\":\"{Constants.JsonRpcVersion}\",\"id\":\"{Guid.NewGuid()}\",\"method\":\"{Constants.SubscribeMethod}\",\"params\":[\"{Constants.FuturesChannel}\"]}}";
                var messageBuffer = Encoding.UTF8.GetBytes(payload);
                var segment = new ArraySegment<byte>(messageBuffer);

                await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, stoppingToken);
                await ReceiveMessagesAsync(webSocket, stoppingToken);
            }
            catch (WebSocketException wsEx)
            {
                _logger.LogWarning(wsEx, "WebSocket connection failed, retrying in {DelaySeconds}s", _options.Value.ReconnectDelaySeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Background service stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in background service");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.Value.ReconnectDelaySeconds), stoppingToken);
        }
    }

    private async Task ReceiveMessagesAsync(ClientWebSocket webSocket, CancellationToken stoppingToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(_options.Value.WebSocketBufferSize);
        try
        {
            var lastPrice = 0m;
            var lastCall = DateTime.UtcNow;
            while (webSocket.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);
                switch (result.MessageType)
                {
                    case WebSocketMessageType.Close:
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, stoppingToken);
                        break;
                    case WebSocketMessageType.Text:
                        var textMessageResult = await HandleWsTextMessage(buffer, result, lastPrice, lastCall);
                        if (textMessageResult != null)
                        {
                            lastPrice = textMessageResult.Value.Price;
                            lastCall = textMessageResult.Value.Timestamp;
                        }

                        break;
                    default:
                        break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<(decimal Price, DateTime Timestamp)?> HandleWsTextMessage(byte[] buffer, WebSocketReceiveResult result, decimal lastPrice, DateTime lastCall)
    {
        try
        {
            if (buffer == null || result.Count <= 0)
            {
                _logger.LogWarning("Received empty or null WebSocket message");
                return null;
            }

            var messageAsString = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var messageAsLastPriceDTO = ParseMessage(messageAsString);
            if (messageAsLastPriceDTO is null)
            {
                return null;
            }

            if (!IsMessageValid(messageAsLastPriceDTO, lastPrice, lastCall))
            {
                return null;
            }

            var price = Math.Floor(messageAsLastPriceDTO.LastPrice / _options.Value.Factor) * _options.Value.Factor;

            using var scope = _scopeFactory?.CreateScope();
            if (scope == null)
            {
                _logger.LogError("Failed to create service scope");
                return null;
            }

            var (options, apiService) = GetScopedServices(scope);

            var user = await apiService.GetUser(options.Key, options.Passphrase, options.Secret);
            if (user == null || user.balance == 0)
            {
                return null;
            }

            await ProcessMarginManagement(apiService, options, messageAsLastPriceDTO, user);
            await ProcessTradeExecution(apiService, options, messageAsLastPriceDTO, user);

            return (price, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WebSocket text message");
            return null;
        }
    }

    private bool IsMessageValid(LastPriceData messageData, decimal lastPrice, DateTime lastCall)
    {
        var messageTimeDifference = DateTime.UtcNow - (messageData.Time?.TimeStampToDateTime() ?? DateTime.MinValue);
        if (messageTimeDifference >= TimeSpan.FromSeconds(_options.Value.MessageTimeoutSeconds))
        {
            return false;
        }

        var price = Math.Floor(messageData.LastPrice / _options.Value.Factor) * _options.Value.Factor;
        if (price == lastPrice)
        {
            return false;
        }

        if ((DateTime.UtcNow - lastCall).TotalSeconds < _options.Value.MinCallIntervalSeconds)
        {
            return false;
        }

        return true;
    }

    private (LnMarketsOptions Options, ILnMarketsApiService ApiService) GetScopedServices(IServiceScope scope)
    {
        var optionsMonitor = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<LnMarketsOptions>>();
        var options = optionsMonitor.CurrentValue;
        var apiService = scope.ServiceProvider.GetService<ILnMarketsApiService>() ??
            throw new InvalidOperationException("ILnMarketsApiService not registered in DI container");
        return (options, apiService);
    }

    private async Task ProcessMarginManagement(ILnMarketsApiService apiService, LnMarketsOptions options, LastPriceData messageData, UserModel user)
    {
        try
        {
            var addedMarginInUsd = 0m;
            var runningTrades = await apiService.FuturesGetRunningTradesAsync(options.Key, options.Passphrase, options.Secret);

            foreach (var runningTrade in runningTrades)
            {
                if (runningTrade.margin <= 0)
                {
                    _logger.LogWarning("Skipping trade {TradeId} with invalid margin: {Margin}", runningTrade.id, runningTrade.margin);
                    continue;
                }

                var loss = (runningTrade.pl / runningTrade.margin) * 100;
                if (loss <= options.MaxLossInPercent)
                {
                    var margin = CalculateMarginToAdd(messageData.LastPrice, runningTrade, options);
                    if (margin > 0)
                    {
                        var amount = (int)(margin * options.AddMarginInUsd);
                        _ = await apiService.AddMargin(options.Key, options.Passphrase, options.Secret, runningTrade.id, amount);
                        addedMarginInUsd += options.AddMarginInUsd;
                    }
                }
            }

            if (addedMarginInUsd > 0 && user.synthetic_usd_balance > addedMarginInUsd)
            {
                _ = await apiService.SwapUsdInBtc(options.Key, options.Passphrase, options.Secret, (int)addedMarginInUsd);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during margin management");
        }
    }

    private decimal CalculateMarginToAdd(decimal currentPrice, FuturesTradeModel runningTrade, LnMarketsOptions options)
    {
        if (currentPrice <= 0)
        {
            _logger.LogWarning("Invalid current price: {Price}", currentPrice);
            return 0;
        }

        if (runningTrade.price <= 0 || runningTrade.quantity <= 0)
        {
            _logger.LogWarning("Invalid trade data - Price: {Price}, Quantity: {Quantity}", runningTrade.price, runningTrade.quantity);
            return 0;
        }

        var btcInSat = Constants.SatoshisPerBitcoin;
        var margin = Math.Round(btcInSat / currentPrice);
        var maxMargin = (btcInSat / runningTrade.price) * runningTrade.quantity;

        if (margin + runningTrade.margin > maxMargin)
        {
            margin = maxMargin - runningTrade.margin;
        }

        return Math.Max(0, margin);
    }

    private async Task ProcessTradeExecution(ILnMarketsApiService apiService, LnMarketsOptions options, LastPriceData messageData, UserModel user)
    {
        try
        {
            if (options.Pause)
            {
                return;
            }

            if (messageData.LastPrice <= 0)
            {
                _logger.LogWarning("Invalid last price: {Price}", messageData.LastPrice);
                return;
            }

            var tradePrice = Math.Floor(messageData.LastPrice / options.Factor) * options.Factor;
            var runningTrades = await apiService.FuturesGetRunningTradesAsync(options.Key, options.Passphrase, options.Secret);
            var currentTrade = runningTrades.FirstOrDefault(x => x.price == tradePrice);

            if (currentTrade != null || runningTrades.Count() > options.MaxRunningTrades)
            {
                return;
            }

            var btcInSat = Constants.SatoshisPerBitcoin;
            var oneUsdInSats = btcInSat / messageData.LastPrice;
            var openTrades = await apiService.FuturesGetOpenTradesAsync(options.Key, options.Passphrase, options.Secret);
            var freeMargin = CalculateFreeMargin(user, openTrades, runningTrades);

            if (freeMargin <= oneUsdInSats)
            {
                return;
            }

            var openTrade = openTrades.FirstOrDefault(x => x.price == tradePrice);
            if (openTrade is null && tradePrice + options.Takeprofit < options.MaxTakeprofitPrice)
            {
                await CancelAllOpenTrades(apiService, options, openTrades);
                _ = await apiService.CreateLimitBuyOrder(
                    options.Key,
                    options.Passphrase,
                    options.Secret,
                    tradePrice,
                    tradePrice + options.Takeprofit,
                    options.Leverage,
                    options.Quantity);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during trade execution");
        }
    }

    private decimal CalculateFreeMargin(UserModel user, IEnumerable<FuturesTradeModel> openTrades, IEnumerable<FuturesTradeModel> runningTrades)
    {
        var btcInSat = Constants.SatoshisPerBitcoin;
        var realMargin = Math.Round(
            openTrades.Select(x => ((btcInSat / x.price) * x.quantity) + x.maintenance_margin).Sum() +
            runningTrades.Select(x => ((btcInSat / x.price) * x.quantity) + x.maintenance_margin).Sum());
        return user.balance - realMargin;
    }

    private async Task CancelAllOpenTrades(ILnMarketsApiService apiService, LnMarketsOptions options, IEnumerable<FuturesTradeModel> openTrades)
    {
        foreach (var oldTrade in openTrades)
        {
            try
            {
                _ = await apiService.Cancel(options.Key, options.Passphrase, options.Secret, oldTrade.id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cancel trade {TradeId}", oldTrade.id);
            }
        }
    }

    private LastPriceData? ParseMessage(string jsonMessage)
    {
        var messageType = DetermineMessageType(jsonMessage);
        switch (messageType)
        {
            case "JsonRpcSubscription":
                var subscription = JsonSerializer.Deserialize<JsonRpcSubscription>(jsonMessage);
                return subscription != null ? HandleJsonRpcSubscription(subscription) : null;
            case "JsonRpcResponse":
            default:
                return null;
        }
    }

    private string DetermineMessageType(string jsonMessage)
    {
        using var doc = JsonDocument.Parse(jsonMessage);
        if (doc.RootElement.TryGetProperty("result", out _))
        {
            return "JsonRpcResponse";
        }
        else if (doc.RootElement.TryGetProperty("method", out _) && doc.RootElement.TryGetProperty("params", out _))
        {
            return "JsonRpcSubscription";
        }

        return "Unknown";
    }

    private LastPriceData? HandleJsonRpcSubscription(JsonRpcSubscription subscription)
    {
        try
        {
            return JsonSerializer.Deserialize<LastPriceData>(subscription.Params.Data.GetRawText());
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize LastPriceData from subscription data: {RawData}", subscription.Params.Data.GetRawText());
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while parsing subscription data: {RawData}", subscription.Params.Data.GetRawText());
            return null;
        }
    }
}
