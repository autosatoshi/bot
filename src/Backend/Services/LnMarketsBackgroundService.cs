using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using Microsoft.Extensions.Options;

namespace AutoBot.Services;

public class LnMarketsBackgroundService(IServiceScopeFactory scopeFactory, ILogger<LnMarketsBackgroundService> logger) : BackgroundService
    {
        private static class Constants
        {
            public const int ReconnectDelaySeconds = 15;
            public const int WebSocketBufferSize = 1024 * 4;
            public const int MessageTimeoutSeconds = 5;
            public const decimal PriceRoundingFactor = 50m;
            public const int MinCallIntervalSeconds = 10;
            public const int SatoshisPerBitcoin = 100_000_000;
            public const string JsonRpcVersion = "2.0";
            public const string SubscribeMethod = "v1/public/subscribe";
            public const string FuturesChannel = "futures:btc_usd:last-price";
        }

        private readonly Uri _serverUri = new("wss://api.lnmarkets.com");
        private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
        private readonly ILogger<LnMarketsBackgroundService> _logger = logger;

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
                    _logger.LogWarning(wsEx, "WebSocket connection failed, retrying in {DelaySeconds}s", Constants.ReconnectDelaySeconds);
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

                await Task.Delay(TimeSpan.FromSeconds(Constants.ReconnectDelaySeconds), stoppingToken);
            }
        }

        private async Task ReceiveMessagesAsync(ClientWebSocket webSocket, CancellationToken stoppingToken)
        {
            var buffer = new byte[Constants.WebSocketBufferSize];

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
                            lastPrice = textMessageResult.Item1;
                            lastCall = textMessageResult.Item2;
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        private async Task<Tuple<decimal, DateTime>?> HandleWsTextMessage(byte[] buffer, WebSocketReceiveResult result, decimal lastPrice, DateTime lastCall)
        {
            var messageAsString = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var messageAsLastPriceDTO = ParseMessage(messageAsString);
            if (messageAsLastPriceDTO is null)
                return null;

            var messageTimeDifference = DateTime.Now - messageAsLastPriceDTO.Time.TimeStampToDateTime();
            if (messageTimeDifference >= TimeSpan.FromSeconds(Constants.MessageTimeoutSeconds))
                return null;

            var price = Math.Floor(messageAsLastPriceDTO.LastPrice / Constants.PriceRoundingFactor) * Constants.PriceRoundingFactor;
            if (price == lastPrice)
                return null;

            if ((DateTime.UtcNow - lastCall).TotalSeconds < Constants.MinCallIntervalSeconds)
                return null;

            using var scope = _scopeFactory.CreateScope();

            var optionsMonitor = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<LnMarketsOptions>>();
            var options = optionsMonitor.CurrentValue;

            var apiService = scope.ServiceProvider.GetService<ILnMarketsApiService>() ?? throw new NullReferenceException();

            var user = await apiService.GetUser(options.Key, options.Passphrase, options.Secret);
            if (user.balance == 0)
                return null;

            var btcInSat = Constants.SatoshisPerBitcoin;

            var addedMarginInUsd = 0m;
            var runningTrades = await apiService.FuturesGetRunningTradesAsync(options.Key, options.Passphrase, options.Secret);
            foreach (var runningTrade in runningTrades)
            {
                var loss = (runningTrade.pl / runningTrade.margin) * 100;
                if (loss <= options.MaxLossInPercent)
                {
                    var margin = Math.Round(btcInSat / messageAsLastPriceDTO.LastPrice);
                    var maxMargin = (btcInSat / runningTrade.price) * runningTrade.quantity;
                    if (margin + runningTrade.margin > maxMargin)
                        margin = maxMargin - runningTrade.margin;

                    var amount = (int)(margin * options.AddMarginInUsd);
                    _ = await apiService.AddMargin(options.Key, options.Passphrase, options.Secret, runningTrade.id, amount);
                    addedMarginInUsd += options.AddMarginInUsd;
                }
            }
            if (addedMarginInUsd > 0 && user.synthetic_usd_balance > addedMarginInUsd)
                _ = await apiService.SwapUsdInBtc(options.Key, options.Passphrase, options.Secret, (int)addedMarginInUsd);

            var tradePrice = Math.Floor(messageAsLastPriceDTO.LastPrice / options.Factor) * options.Factor;
            var currentTrade = runningTrades.Where(x => x.price == tradePrice).FirstOrDefault();
            var oneUsdInSats = btcInSat / messageAsLastPriceDTO.LastPrice;
            var openTrades = await apiService.FuturesGetOpenTradesAsync(options.Key, options.Passphrase, options.Secret);
            var realMargin = Math.Round(openTrades.Select(x => ((btcInSat / x.price) * x.quantity)
                + x.maintenance_margin).Sum()
                + runningTrades.Select(x => ((btcInSat / x.price) * x.quantity)
                + x.maintenance_margin).Sum());
            var freeMargin = user.balance - realMargin;
            if (currentTrade == null && runningTrades.Count() <= options.MaxRunningTrades && freeMargin > oneUsdInSats && !options.Pause)
            {
                var openTrade = openTrades.Where(x => x.price == tradePrice).FirstOrDefault();
                if (openTrade is null && tradePrice + options.Takeprofit < options.MaxTakeprofitPrice)
                {
                    foreach (var oldTrade in openTrades)
                        _ = await apiService.Cancel(options.Key, options.Passphrase, options.Secret, oldTrade.id);
                    _ = await apiService.CreateLimitBuyOrder(options.Key, options.Passphrase, options.Secret, tradePrice, tradePrice + options.Takeprofit, options.Leverage, options.Quantity);
                }
            }

            return new Tuple<decimal, DateTime>(price, DateTime.UtcNow);
        }

        private LastPriceData? ParseMessage(string jsonMessage)
        {
            var messageType = DetermineMessageType(jsonMessage);
            switch (messageType)
            {
                case "JsonRpcSubscription":
                    var subscription = JsonSerializer.Deserialize<JsonRpcSubscription>(jsonMessage);
                    return HandleJsonRpcSubscription(subscription);
                case "JsonRpcResponse":
                default:
                    return null;
            }
        }

        private string DetermineMessageType(string jsonMessage)
        {
            using var doc = JsonDocument.Parse(jsonMessage);
            if (doc.RootElement.TryGetProperty("result", out _))
                return "JsonRpcResponse";
            else if (doc.RootElement.TryGetProperty("method", out _) && doc.RootElement.TryGetProperty("params", out _))
                return "JsonRpcSubscription";
            return "Unknown";
        }

        private LastPriceData? HandleJsonRpcSubscription(JsonRpcSubscription subscription)
        {
            return JsonSerializer.Deserialize<LastPriceData>(subscription.Params.Data.GetRawText());
        }
    }
