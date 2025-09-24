using System.Collections.Concurrent;
using AutoBot;
using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using AutoBot.Services;
using Microsoft.Extensions.Options;

public class TradeManager : ITradeManager, IDisposable
{
    private static class Constants
    {
        public const int SatoshisPerBitcoin = 100_000_000;
    }

    private readonly CancellationTokenSource _exitTokenSource = new();
    private readonly BlockingCollection<LastPriceData> _queue = new();
    private readonly Task _updateLoop;
    private readonly ILogger<TradeManager> _logger;
    private DateTime _lastConfigChange = DateTime.MinValue;

    public TradeManager(ILnMarketsApiService client, IOptionsMonitor<LnMarketsOptions> options, ILogger<TradeManager> logger)
    {
        _logger = logger;

        // Log configuration changes with 500ms debouncing (.OnChange triggers multiple times for the same change...)
        options.OnChange(newOptions =>
        {
            var now = DateTime.UtcNow;
            if ((now - _lastConfigChange).TotalMilliseconds > 500)
            {
                _logger.LogWarning(
                    "LnMarketsOptions configuration updated:\n\tPause={},\n\tQuantity={},\n\tLeverage={},\n\tTakeProfit={},\n\tMaxTakeprofitPrice={},\n\tMaxRunningTrades={},\n\tFactor={},\n\tAddMarginInUsd={}",
                    newOptions.Pause,
                    newOptions.Quantity,
                    newOptions.Leverage,
                    newOptions.Takeprofit,
                    newOptions.MaxTakeprofitPrice,
                    newOptions.MaxRunningTrades,
                    newOptions.Factor,
                    newOptions.AddMarginInUsd);
                _lastConfigChange = now;
            }
        });

        _updateLoop = Task.Run(async () =>
        {
            decimal lastPrice = 0;
            var lastIteration = DateTime.MinValue;
            while (!_exitTokenSource.IsCancellationRequested)
            {
                try
                {
                    var data = _queue.Take(_exitTokenSource.Token);
                    if (data.LastPrice == lastPrice)
                    {
                        continue;
                    }

                    lastPrice = data.LastPrice;

                    var timestamp = data.Time?.TimeStampToDateTime() ?? DateTime.MinValue;
                    var timeDelta = DateTime.UtcNow - timestamp.ToUniversalTime();
                    if (timeDelta.TotalSeconds >= options.CurrentValue.MessageTimeoutSeconds)
                    {
                        continue;
                    }

                    if ((DateTime.UtcNow - lastIteration).TotalSeconds < options.CurrentValue.MinCallIntervalSeconds)
                    {
                        continue;
                    }

                    await HandlePriceUpdate(data, client, options, logger);

                    lastIteration = DateTime.UtcNow;
                }
                catch (OperationCanceledException) when (_exitTokenSource.IsCancellationRequested)
                {
                    logger.LogDebug("Exiting update loop.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occured handling new price data");
                }
            }
        });
    }

    public void UpdatePrice(LastPriceData data)
    {
        if (!_queue.IsAddingCompleted)
        {
            _queue.Add(data);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        try
        {
            _queue.CompleteAdding();
            _exitTokenSource.Cancel();
            if (!_updateLoop.Wait(TimeSpan.FromSeconds(10)))
            {
                _logger.LogWarning("The update loop didn't finish within timeout");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"An error occured disposing {nameof(TradeManager)}");
        }
        finally
        {
            _exitTokenSource.Dispose();
            _queue.Dispose();
            _updateLoop.Dispose();
        }

        _logger.LogDebug($"Successfully disposed {nameof(TradeManager)}");
    }

    private static async Task HandlePriceUpdate(LastPriceData data, ILnMarketsApiService client, IOptionsMonitor<LnMarketsOptions> options, ILogger? logger = null)
    {
        logger?.LogInformation("Handling price update: {}$", data.LastPrice);

        if (options.CurrentValue.Pause)
        {
            return;
        }

        var user = await client.GetUser(options.CurrentValue.Key, options.CurrentValue.Passphrase, options.CurrentValue.Secret);
        if (user == null || user.balance == 0)
        {
            return;
        }

        await ProcessMarginManagement(client, options.CurrentValue, data, user, logger);
        await ProcessTradeExecution(client, options.CurrentValue, data, user, logger);
    }

    private static async Task ProcessMarginManagement(ILnMarketsApiService client, LnMarketsOptions options, LastPriceData messageData, UserModel user, ILogger? logger = null)
    {
        try
        {
            var addedMarginInUsd = 0m;
            var runningTrades = await client.GetRunningTrades(options.Key, options.Passphrase, options.Secret);

            foreach (var runningTrade in runningTrades)
            {
                if (runningTrade.margin <= 0)
                {
                    logger?.LogWarning("Skipping trade {TradeId} with invalid margin: {Margin}", runningTrade.id, runningTrade.margin);
                    continue;
                }

                var loss = (runningTrade.pl / runningTrade.margin) * 100;
                if (loss <= options.MaxLossInPercent)
                {
                    var margin = CalculateMarginToAdd(messageData.LastPrice, runningTrade);
                    if (margin > 0)
                    {
                        var amount = (int)(margin * options.AddMarginInUsd);
                        if (await client.AddMargin(options.Key, options.Passphrase, options.Secret, runningTrade.id, amount))
                        {
                            logger?.LogInformation("Successfully added margin {} to running trade '{}'", amount, runningTrade.id);
                            addedMarginInUsd += options.AddMarginInUsd;
                        }
                    }
                }
            }

            if (addedMarginInUsd > 0 && user.synthetic_usd_balance > addedMarginInUsd)
            {
                if (await client.SwapUsdInBtc(options.Key, options.Passphrase, options.Secret, (int)addedMarginInUsd))
                {
                    logger?.LogInformation("Successfully swapped {}$ to btc", addedMarginInUsd);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error during margin management");
        }
    }

    private static decimal CalculateMarginToAdd(decimal currentPrice, FuturesTradeModel runningTrade, ILogger? logger = null)
    {
        if (currentPrice <= 0)
        {
            logger?.LogWarning("Invalid current price: {Price}", currentPrice);
            return 0;
        }

        if (runningTrade.price <= 0 || runningTrade.quantity <= 0)
        {
            logger?.LogWarning("Invalid trade data - Price: {Price}, Quantity: {Quantity}", runningTrade.price, runningTrade.quantity);
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

    private static async Task ProcessTradeExecution(ILnMarketsApiService apiService, LnMarketsOptions options, LastPriceData messageData, UserModel user, ILogger? logger = null)
    {
        try
        {
            if (messageData.LastPrice <= 0)
            {
                logger?.LogWarning("Invalid last price: {Price}", messageData.LastPrice);
                return;
            }

            var tradePrice = Math.Floor(messageData.LastPrice / options.Factor) * options.Factor;
            var runningTrades = await apiService.GetRunningTrades(options.Key, options.Passphrase, options.Secret);
            var currentTrade = runningTrades.FirstOrDefault(x => x.price == tradePrice);

            if (currentTrade != null || runningTrades.Count() > options.MaxRunningTrades)
            {
                return;
            }

            const int SatoshisPerBitcoin = 100_000_000;
            var oneUsdInSats = SatoshisPerBitcoin / messageData.LastPrice;
            var openTrades = await apiService.GetOpenTrades(options.Key, options.Passphrase, options.Secret);
            var freeMargin = CalculateFreeMargin(user, openTrades, runningTrades);

            if (freeMargin <= oneUsdInSats)
            {
                return;
            }

            var openTrade = openTrades.FirstOrDefault(x => x.price == tradePrice);
            if (openTrade is null && tradePrice + options.Takeprofit < options.MaxTakeprofitPrice)
            {
                foreach (var oldTrade in openTrades)
                {
                    try
                    {
                        _ = await apiService.Cancel(options.Key, options.Passphrase, options.Secret, oldTrade.id);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "Failed to cancel trade {TradeId}", oldTrade.id);
                    }
                }

                if (await apiService.CreateLimitBuyOrder(
                    options.Key,
                    options.Passphrase,
                    options.Secret,
                    tradePrice,
                    tradePrice + options.Takeprofit,
                    options.Leverage,
                    options.Quantity))
                {
                    logger?.LogInformation("Successfully created limit buy order:\n\t[price: '{}', takeprofit: '{}', leverage: '{}', quantity: '{}']", tradePrice, tradePrice + options.Takeprofit, options.Leverage, options.Quantity);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error during trade execution");
        }
    }

    private static decimal CalculateFreeMargin(UserModel user, IEnumerable<FuturesTradeModel> openTrades, IEnumerable<FuturesTradeModel> runningTrades)
    {
        var btcInSat = Constants.SatoshisPerBitcoin;
        var realMargin = Math.Round(
            openTrades.Select(x => ((btcInSat / x.price) * x.quantity) + x.maintenance_margin).Sum() +
            runningTrades.Select(x => ((btcInSat / x.price) * x.quantity) + x.maintenance_margin).Sum());
        return user.balance - realMargin;
    }
}
