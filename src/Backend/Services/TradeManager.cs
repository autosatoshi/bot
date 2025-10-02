using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using Microsoft.Extensions.Options;

namespace AutoBot.Services;

public class TradeManager : ITradeManager
{
    private static class Constants
    {
        public const int SatoshisPerBitcoin = 100_000_000;
    }

    private readonly ILnMarketsApiService _client;
    private readonly IOptionsMonitor<LnMarketsOptions> _options;
    private readonly ILogger<TradeManager> _logger;
    private DateTime _lastConfigChange = DateTime.MinValue;

    public TradeManager(ILnMarketsApiService client, IOptionsMonitor<LnMarketsOptions> options, ILogger<TradeManager> logger)
    {
        _client = client;
        _options = options;
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
    }

    public async Task HandlePriceUpdateAsync(LastPriceData data)
    {
        await HandlePriceUpdate(data, _client, _options.CurrentValue, _logger);
    }

    private static async Task HandlePriceUpdate(LastPriceData data, ILnMarketsApiService client, LnMarketsOptions options, ILogger? logger = null)
    {
        logger?.LogInformation("Handling price update: {}$", data.LastPrice);

        if (options.Pause)
        {
            return;
        }

        var user = await client.GetUser(options.Key, options.Passphrase, options.Secret);
        if (user == null || user.balance == 0)
        {
            return;
        }

        await ProcessMarginManagement(client, options, data, user, logger);
        await ProcessTradeExecution(client, options, data, user, logger);
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

            var tradePriceInUsd = Math.Floor(messageData.LastPrice / options.Factor) * options.Factor;
            var runningTrades = await apiService.GetRunningTrades(options.Key, options.Passphrase, options.Secret);
            var currentTrade = runningTrades.FirstOrDefault(x => x.price == tradePriceInUsd);

            if (currentTrade != null || runningTrades.Count() >= options.MaxRunningTrades)
            {
                return;
            }

            var oneUsdInSats = Constants.SatoshisPerBitcoin / messageData.LastPrice;
            var openTrades = await apiService.GetOpenTrades(options.Key, options.Passphrase, options.Secret);
            var freeMarginInSats = CalculateFreeMargin(user, openTrades, runningTrades);

            if (freeMarginInSats <= oneUsdInSats)
            {
                return;
            }

            var openTrade = openTrades.FirstOrDefault(x => x.price == tradePriceInUsd);
            if (openTrade != null)
            {
                logger?.LogInformation("Skipping trade execution for price {Price}$ because an open trade with the same price already exists.", tradePriceInUsd);
                return;
            }

            var exitPriceInUsd = ((Func<decimal>)(() =>
            {
                if (!options.TargetNetPLInSats.HasValue)
                {
                    return tradePriceInUsd + options.Takeprofit;
                }

                var feeRate = GetFeeRateFromTier(user.fee_tier);
                logger?.LogInformation("User fee tier: {FeeTier}, mapped to fee rate: {FeeRate:P}", user.fee_tier, feeRate);

                var targetNetPLInSats = options.TargetNetPLInSats.Value;
                var adjustedExitPriceInUsd = TradeFactory.CalculateExitPriceForTargetNetPL(options.Quantity, tradePriceInUsd, options.Leverage, feeRate, targetNetPLInSats, TradeSide.Buy);
                logger?.LogInformation("Adjusted exit price to {AdjustedExitPrice}$ for a net P&L of {TargetProfit} sats", adjustedExitPriceInUsd, targetNetPLInSats);
                return Math.Round(adjustedExitPriceInUsd, 0, MidpointRounding.AwayFromZero);
            }))();

            if (exitPriceInUsd >= options.MaxTakeprofitPrice)
            {
                return;
            }

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
                tradePriceInUsd,
                exitPriceInUsd,
                options.Leverage,
                options.Quantity))
            {
                logger?.LogInformation("Successfully created limit buy order:\n\t[price: '{}', takeprofit: '{}', leverage: '{}', quantity: '{}']", tradePriceInUsd, exitPriceInUsd, options.Leverage, options.Quantity);
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

    private static decimal GetFeeRateFromTier(decimal feeTier)
    {
        // LN Markets fee tiers (based on 30-day cumulative volume):
        // API returns 0-indexed tiers:
        // 0 = Tier 1: 0 volume → 0.1% fee
        // 1 = Tier 2: > $250k → 0.08% fee
        // 2 = Tier 3: > $1,000k → 0.07% fee
        // 3 = Tier 4: > $5,000k → 0.06% fee
        return feeTier switch
        {
            0 => 0.001m,   // Tier 1: 0.1%
            1 => 0.0008m,  // Tier 2: 0.08%
            2 => 0.0007m,  // Tier 3: 0.07%
            3 => 0.0006m,  // Tier 4: 0.06%
            _ => 0.001m,   // Default to highest fee rate for safety
        };
    }
}
