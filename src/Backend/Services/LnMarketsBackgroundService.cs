namespace AutoBot.Services
{
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System;
    using AutoBot.Models.LnMarkets;

    public class LnMarketsBackgroundService(IServiceScopeFactory scopeFactory) : BackgroundService
    {
        private readonly Uri _serverUri = new("wss://api.lnmarkets.com");
        private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var webSocket = new ClientWebSocket();

                try
                {
                    await webSocket.ConnectAsync(_serverUri, stoppingToken);

                    var payload = $"{{\"jsonrpc\":\"2.0\",\"id\":\"{Guid.NewGuid()}\",\"method\":\"v1/public/subscribe\",\"params\":[\"futures:btc_usd:last-price\"]}}";
                    var messageBuffer = Encoding.UTF8.GetBytes(payload);
                    var segment = new ArraySegment<byte>(messageBuffer);

                    await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, stoppingToken);
                    await ReceiveMessagesAsync(webSocket, stoppingToken);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }

                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }

        private async Task ReceiveMessagesAsync(ClientWebSocket webSocket, CancellationToken stoppingToken)
        {
            var buffer = new byte[1024 * 4];

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
            if (messageTimeDifference >= TimeSpan.FromSeconds(5))
                return null;

            var price = Math.Floor(messageAsLastPriceDTO.LastPrice / 50) * 50;
            if (price == lastPrice)
                return null;

            if ((DateTime.UtcNow - lastCall).TotalSeconds < 10)
                return null;

            using var scope = _scopeFactory.CreateScope();

            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>() ?? throw new ArgumentNullException();

            var key = configuration.GetValue<string>("ln:key") ?? throw new ArgumentNullException();
            var passphrase = configuration.GetValue<string>("ln:passphrase") ?? throw new ArgumentNullException();
            var secret = configuration.GetValue<string>("ln:secret") ?? throw new ArgumentNullException();
            var pause = configuration.GetValue<bool>("ln:pause");
            var quantity = configuration.GetValue<int>("ln:quantity");
            var leverage = configuration.GetValue<int>("ln:leverage");
            var takeprofit = configuration.GetValue<int>("ln:takeprofit");
            var maxTakeprofitPrice = configuration.GetValue<int>("ln:maxTakeprofitPrice");
            var maxRunningTrades = configuration.GetValue<int>("ln:maxRunningTrades");
            var factor = configuration.GetValue<int>("ln:factor");
            var addMarginInUsd = configuration.GetValue<int>("ln:addMarginInUsd");
            var maxLossInPercent = configuration.GetValue<int>("ln:maxLossInPercent");

            var apiService = scope.ServiceProvider.GetService<ILnMarketsApiService>() ?? throw new NullReferenceException();

            var user = await apiService.GetUser(key, passphrase, secret);
            if (user.balance == 0)
                return null;

            var btcInSat = 100000000;

            var addedMarginInUsd = 0;
            var runningTrades = await apiService.FuturesGetRunningTradesAsync(key, passphrase, secret);
            foreach (var runningTrade in runningTrades)
            {
                var loss = (runningTrade.pl / runningTrade.margin) * 100;
                if (loss <= maxLossInPercent)
                {
                    var margin = Math.Round(btcInSat / messageAsLastPriceDTO.LastPrice);
                    var maxMargin = (btcInSat / runningTrade.price) * runningTrade.quantity;
                    if (margin + runningTrade.margin > maxMargin)
                        margin = maxMargin - runningTrade.margin;

                    _ = await apiService.AddMargin(key, passphrase, secret, runningTrade.id, margin * addMarginInUsd);
                    addedMarginInUsd += addMarginInUsd;
                }
            }
            if (addedMarginInUsd > 0 && user.synthetic_usd_balance > addedMarginInUsd)
                _ = await apiService.SwapUsdInBtc(key, passphrase, secret, addedMarginInUsd);

            var tradePrice = Math.Floor(messageAsLastPriceDTO.LastPrice / factor) * factor;
            var currentTrade = runningTrades.Where(x => x.price == tradePrice).FirstOrDefault();
            var oneUsdInSats = btcInSat / messageAsLastPriceDTO.LastPrice;
            var openTrades = await apiService.FuturesGetOpenTradesAsync(key, passphrase, secret);
            var realMargin = Math.Round(openTrades.Select(x => ((btcInSat / x.price) * x.quantity)
                + x.maintenance_margin).Sum()
                + runningTrades.Select(x => ((btcInSat / x.price) * x.quantity)
                + x.maintenance_margin).Sum());
            var freeMargin = user.balance - realMargin;
            if (currentTrade == null && runningTrades.Count() <= maxRunningTrades && freeMargin > oneUsdInSats && !pause)
            {
                var openTrade = openTrades.Where(x => x.price == tradePrice).FirstOrDefault();
                if (openTrade is null && tradePrice + takeprofit < maxTakeprofitPrice)
                {
                    foreach (var oldTrade in openTrades)
                        _ = await apiService.Cancel(key, passphrase, secret, oldTrade.id);
                    _ = await apiService.CreateLimitBuyOrder(key, passphrase, secret, tradePrice, tradePrice + takeprofit, leverage, quantity);
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
}
