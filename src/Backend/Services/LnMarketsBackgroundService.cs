namespace AutoBot.Services
{
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json.Serialization;
    using System.Text.Json;

    public class LnMarketsBackgroundService : BackgroundService
    {
        private readonly Uri _serverUri;
        IServiceScopeFactory _scopeFactory;

        public LnMarketsBackgroundService(IServiceScopeFactory scopeFactory)
        {
            _serverUri = new Uri("wss://api.lnmarkets.com");
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using (var webSocket = new ClientWebSocket())
                {
                    try
                    {
                        await webSocket.ConnectAsync(_serverUri, stoppingToken);

                        var payload = $"{{\"jsonrpc\": \"2.0\", \"id\": \"{Guid.NewGuid()}\", \"method\": \"v1/public/subscribe\"," +
                            "\"params\": [\"futures:btc_usd:last-price\"]}";

                        var messageBuffer = Encoding.UTF8.GetBytes(payload);
                        var segment = new ArraySegment<byte>(messageBuffer);

                        await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, stoppingToken);
                        await ReceiveMessagesAsync(webSocket, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }

        private async Task ReceiveMessagesAsync(ClientWebSocket webSocket, CancellationToken stoppingToken)
        {
            var buffer = new byte[1024 * 4];

            decimal lastPrice = 0;
            var btcInSat = 100000000;
            var lastCall = DateTime.UtcNow;
            while (webSocket.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, stoppingToken);
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var lastprice = ParseMessage(message);
                    if (lastprice is not null)
                    {
                        var timeDif = DateTime.Now - lastprice.Time.TimeStampToDateTime();
                        if (timeDif <= TimeSpan.FromSeconds(5))
                        {
                            Console.WriteLine($"${lastprice.LastPrice:N0} {(DateTime.Now - lastprice.Time.TimeStampToDateTime())}");
                            using var scope = _scopeFactory.CreateScope();
                            var apiService = scope.ServiceProvider.GetService<ILnMarketsApiService>() ?? throw new NullReferenceException();

                            var price = Math.Floor(lastprice.LastPrice / 50) * 50;
                            var call = false;

                            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                            var key = configuration.GetValue<string>("ln:key");
                            var passphrase = configuration.GetValue<string>("ln:passphrase");
                            var secret = configuration.GetValue<string>("ln:secret");
                            var pause = configuration.GetValue<bool>("ln:pause");

                            if (lastPrice != price && (DateTime.UtcNow - lastCall).TotalSeconds > 10)
                            {
                                Console.WriteLine($"\n\n------------------- {Math.Round(price)}$ {Math.Round((DateTime.UtcNow - lastCall).TotalSeconds)} -------------------\n");

                                var from = DateTime.UtcNow.AddYears(-1).ToUnixTimeInMilliseconds();
                                var to = DateTime.UtcNow.ToUnixTimeInMilliseconds();

                                var user = await apiService.GetUser(key, passphrase, secret);
                                Console.WriteLine("GetUser done");
                                if (user.balance == 0)
                                    return;

                                var deposits = await apiService.GetDeposits(key, passphrase, secret);
                                Console.WriteLine("GetDeposits done");
                                var runningTrades = await apiService.FuturesGetRunningTradesAsync(key, passphrase, secret, from, to);
                                Console.WriteLine("FuturesGetRunningTrades done");
                                var openTrades = await apiService.FuturesGetOpenTradesAsync(key, passphrase, secret, from, to);
                                Console.WriteLine("FuturesGetOpenTrades done");

                                var totalDepositInSAT = deposits.Where(x => x.success).Select(x => x.amount).Sum();
                                Console.WriteLine("totalDepositInSAT done");
                                totalDepositInSAT += deposits.Where(x => x.type == "bitcoin")?.Select(x => x.amount)?.Sum() ?? 0;
                                var totalDepositInUSD = Math.Round(((decimal)totalDepositInSAT / (decimal)btcInSat) * lastprice.LastPrice, 2);
                                var currentBalanceInSat = user.balance;
                                var currentBalanceInUSD = Math.Round((currentBalanceInSat / btcInSat) * lastprice.LastPrice, 2);
                                var realPl = currentBalanceInSat - totalDepositInSAT;
                                var realROI = Math.Round((realPl / currentBalanceInSat) * 100, 2);
                                var runningPl = runningTrades.Select(x => x.pl).Sum();
                                var runningPLinUSD = Math.Round(((decimal)runningPl / (decimal)btcInSat) * lastprice.LastPrice, 2);
                                var marginUsed = runningTrades.Select(x => x.margin + x.maintenance_margin).Sum() +
                                    openTrades.Select(x => x.margin + x.maintenance_margin).Sum();

                                var runningROI = marginUsed > 0 ? Math.Round((runningPl / marginUsed) * 100, 2) : 0;
                                var realMargin = Math.Round(openTrades.Select(x => ((100000000 / x.price) * x.quantity) + x.maintenance_margin).Sum() +
                                    runningTrades.Select(x => ((100000000 / x.price) * x.quantity) + x.maintenance_margin).Sum());
                                var freeMargin = currentBalanceInSat - realMargin;
                                var totalPL = (marginUsed + user.balance) - totalDepositInSAT;
                                var totalPLinUSD = Math.Round(((decimal)totalPL / (decimal)btcInSat) * lastprice.LastPrice, 2);
                                var totalPLinPercent = Math.Round((totalPL / totalDepositInSAT) * 100, 2);

                                decimal factor = runningTrades.Count() <= 9 ? 250 : 500;
                                var tradePrice = Math.Floor(lastprice.LastPrice / factor) * factor;

                                Console.WriteLine(
                                    $"LastPrice:\t\t\t{lastprice.LastPrice}\n" +
                                    $"USD: \t\t\t{user.synthetic_usd_balance}$\n" +
                                    $"Total deposit:\t\t{totalDepositInSAT} SAT ({totalDepositInUSD}$)\n" +
                                    $"Current balance:\t{user.balance} SAT ({currentBalanceInUSD}$)\n" +
                                    $"Real PL:\t\t{realPl} SAT\n" +
                                    $"Real ROI:\t\t{realROI} %\n" +
                                    $"Total PL:\t\t{totalPL} ({totalPLinPercent}%) ({totalPLinUSD}$)" +
                                    $"\n" +
                                    $"Margin used:\t\t{marginUsed} SAT\n" +
                                    $"Running PL:\t\t{runningPl} SAT\n" +
                                    $"Running PL:\t\t{runningPLinUSD} $\n" +
                                    $"Running ROI:\t\t{runningROI} %\n" +
                                    $"\n" +
                                    $"Real margin:\t\t{realMargin} SAT\n" +
                                    $"Free margin:\t\t{freeMargin} SAT\n" +
                                    $"\n" +
                                    $"running trades:\t\t{runningTrades.Count()}\n" +
                                    $"Open trades:\t\t{openTrades.Count()}\n" +
                                    $"\n" +
                                    $"Trade price:\t\t{tradePrice}\n");

                                var addMarginInUsd = 0;
                                foreach (var runningTrade in runningTrades)
                                {
                                    Console.WriteLine(runningTrade.liquidation);

                                    var loss = (runningTrade.pl / runningTrade.margin) * 100;
                                    Console.WriteLine("loss: " + Math.Round(loss) + " PL: " + runningTrade.pl + " " + runningTrade.leverage + $" {runningTrade.id}");
                                    if (loss <= -50)
                                    {
                                        var margin = Math.Round(100000000 / lastprice.LastPrice);
                                        var maxMargin = (100000000 / runningTrade.price) * runningTrade.quantity;
                                        if (margin + runningTrade.margin > maxMargin)
                                        {
                                            margin = maxMargin - runningTrade.margin;
                                        }

                                        var addMarginResult = await apiService.AddMargin(key, passphrase, secret, runningTrade.id, margin * 3);
                                        addMarginInUsd += 3;
                                        Console.WriteLine($"add margin: {margin}");
                                        Console.Beep();
                                    }
                                }
                                if (addMarginInUsd > 0 && user.synthetic_usd_balance > addMarginInUsd)
                                {
                                    _ = await apiService.SwapUsdInBtc(key, passphrase, secret, addMarginInUsd);
                                    Console.WriteLine($"Swap USD (${addMarginInUsd:N0}) in BTC");
                                }

                                var currentTrade = runningTrades.Where(x => x.price == tradePrice).FirstOrDefault();
                                var oneUsdInSats = btcInSat / lastprice.LastPrice;
                                if (currentTrade == null && runningTrades.Count() <= 19 && freeMargin > oneUsdInSats && !pause)
                                {
                                    var openTrade = openTrades.Where(x => x.price == tradePrice).FirstOrDefault();
                                    Console.WriteLine($"check trades:\t\t{openTrade?.price ?? -1}");
                                    if (openTrade is null && tradePrice + 1500 < 110000)
                                    {
                                        foreach (var oldTrade in openTrades)
                                        {
                                            _ = await apiService.Cancel(key, passphrase, secret, oldTrade.id);
                                            Console.WriteLine($"close old open trade: {oldTrade.price}");
                                        }

                                        _ = await apiService.CreateLimitBuyOrder(key, passphrase, secret, tradePrice, tradePrice + (1500), 80, 270);
                                        Console.WriteLine($"create limit buy order: {tradePrice}");
                                    }
                                }

                                Console.WriteLine("-------------------------------------------------\n");

                                var openSlots = 50 - runningTrades.Count();
                                var testPrice = tradePrice;
                                var totalSats = 0m;
                                for (var i = openSlots; i >= 0; i--)
                                {
                                    var oneUSDinSAT = Math.Round((btcInSat / testPrice) * 10);
                                    var takeProfitPrice = testPrice + 500;
                                    testPrice -= 400;

                                    totalSats += oneUSDinSAT;
                                }

                                var totalSatsInUSD = (totalSats / btcInSat) * lastprice.LastPrice;

                                Console.WriteLine($"total sats: {totalSats:C2} ({totalSatsInUSD:C2})");
                                call = true;
                            }

                            if (call)
                                lastCall = DateTime.UtcNow;
                            lastPrice = price;
                        }
                    }
                }
            }
        }

        public LastPriceData? ParseMessage(string jsonMessage)
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
            using (JsonDocument doc = JsonDocument.Parse(jsonMessage))
            {
                if (doc.RootElement.TryGetProperty("result", out _))
                {
                    return "JsonRpcResponse";
                }
                else if (doc.RootElement.TryGetProperty("method", out _) && doc.RootElement.TryGetProperty("params", out _))
                {
                    return "JsonRpcSubscription";
                }
                else
                {
                    return "Unknown";
                }
            }
        }

        private LastPriceData HandleJsonRpcSubscription(JsonRpcSubscription subscription)
        {
            var lastPriceData = JsonSerializer.Deserialize<LastPriceData>(subscription.Params.Data.GetRawText());
            return lastPriceData;
        }
    }

    public class SubscriptionParams
    {
        [JsonPropertyName("channel")]
        public string Channel { get; set; }

        [JsonPropertyName("data")]
        public JsonElement Data { get; set; } 
    }

    public class JsonRpcSubscription
    {
        [JsonPropertyName("method")]
        public string Method { get; set; }

        [JsonPropertyName("params")]
        public SubscriptionParams Params { get; set; }

        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; }
    }

    public class LastPriceData
    {
        [JsonPropertyName("time")]
        public long Time { get; set; }

        [JsonPropertyName("lastPrice")]
        public decimal LastPrice { get; set; }

        [JsonPropertyName("lastTickDirection")]
        public string LastTickDirection { get; set; }
    }
}
