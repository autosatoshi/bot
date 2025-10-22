using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using AutoBot.Models.Units;
using Microsoft.Extensions.Options;

namespace AutoBot.Services;

public class TradeManager : ITradeManager
{
    private readonly IMarketplaceClient _client;
    private readonly IOptionsMonitor<LnMarketsOptions> _options;
    private readonly ILogger<TradeManager> _logger;
    private DateTime _lastConfigChange = DateTime.MinValue;

    public TradeManager(IMarketplaceClient client, IOptionsMonitor<LnMarketsOptions> options, ILogger<TradeManager> logger)
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
        await HandlePriceUpdate(_client, _options.CurrentValue, data, _logger);
    }

    private static async Task HandlePriceUpdate(IMarketplaceClient client, LnMarketsOptions options, LastPriceData data, ILogger? logger = null)
    {
        logger?.LogInformation("Handling price update: {Price}$", data.LastPrice);

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

    private static async Task ProcessMarginManagement(IMarketplaceClient client, LnMarketsOptions options, LastPriceData data, UserModel user, ILogger? logger = null)
    {
        try
        {
            var runningTrades = await client.GetRunningTrades(options.Key, options.Passphrase, options.Secret);
            if (runningTrades.Count == 0)
            {
                return;
            }

            Satoshi oneUsdInSats = decimal.ToInt64(Math.Round(Constants.SatoshisPerBitcoin.Value / data.LastPrice));
            var marginCallTrades = runningTrades
                .Where(x => x.leverage > 1) // Skip trades with 1x leverage
                .Where(x => x.margin > 0) // Skip trades with invalid margin to prevent division by zero
                .Where(x => (decimal)x.pl.Value / x.margin.Value * 100 <= options.MaxLossInPercent) // Include trades where loss is worse than threshold
                .ToList();

            if (marginCallTrades.Count == 0)
            {
                return;
            }

            Satoshi oneMarginCallInSats = decimal.ToInt64(Math.Round(oneUsdInSats.Value * options.AddMarginInUsd, MidpointRounding.AwayFromZero));
            if (oneMarginCallInSats.Value * marginCallTrades.Count > user.balance)
            {
                logger?.LogWarning("Total amount for margin calls exceeds the available balance. Defaulting to FIFO margin call execution.");
            }

            Satoshi totalAddedMarginInSats = 0;
            Dollar totalAddedMarginInUsd = 0;
            foreach (var trade in marginCallTrades)
            {
                Satoshi maxMarginInSats = decimal.ToInt64(Math.Round(Constants.SatoshisPerBitcoin.Value / trade.price * trade.quantity, MidpointRounding.AwayFromZero));
                if (oneMarginCallInSats + trade.margin > maxMarginInSats)
                {
                    logger?.LogWarning("Margin call of {MarginCall} sats would exceed maximum margin of {MaxMargin} sats for trade {Id}", oneMarginCallInSats, maxMarginInSats, trade.id);
                    continue;
                }

                if (oneMarginCallInSats > user.balance)
                {
                    logger?.LogWarning("Insufficient available balance to execute margin call for trade {Id}: required {Margin} sats | available {Balance} sats", trade.id, oneMarginCallInSats, user.balance);
                    continue;
                }

                if (!await client.AddMarginInSats(options.Key, options.Passphrase, options.Secret, trade.id, oneMarginCallInSats))
                {
                    logger?.LogError("Failed to add margin {Margin} sats to running trade {Id}", oneMarginCallInSats, trade.id);
                    continue;
                }

                user.balance -= oneMarginCallInSats;
                totalAddedMarginInSats += oneMarginCallInSats;
                totalAddedMarginInUsd += options.AddMarginInUsd;
                logger?.LogInformation("Successfully added margin {Margin} sats to running trade {Id}", oneMarginCallInSats, trade.id);
            }

            if (totalAddedMarginInUsd <= 0)
            {
                return;
            }

            if (user.synthetic_usd_balance < totalAddedMarginInUsd)
            {
                logger?.LogDebug("No enough synthetic usd balance available for swap: required {Amount}$ | available {Available}$", totalAddedMarginInUsd, user.synthetic_usd_balance);
                return;
            }

            if (!await client.SwapUsdInBtc(options.Key, options.Passphrase, options.Secret, (int)totalAddedMarginInUsd))
            {
                logger?.LogError("Failed to swap {Amount}$ to btc", totalAddedMarginInUsd);
                return;
            }

            user.balance += totalAddedMarginInSats;
            user.synthetic_usd_balance -= totalAddedMarginInUsd;
            logger?.LogInformation("Successfully swapped {Amount}$ to btc", totalAddedMarginInUsd);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error during margin management");
        }
    }

    private static async Task ProcessTradeExecution(IMarketplaceClient client, LnMarketsOptions options, LastPriceData data, UserModel user, ILogger? logger = null)
    {
        try
        {
            if (data.LastPrice <= 0)
            {
                logger?.LogWarning("Invalid last price: {Price}", data.LastPrice);
                return;
            }

            var runningTrades = await client.GetRunningTrades(options.Key, options.Passphrase, options.Secret);
            if (runningTrades.Count >= options.MaxRunningTrades)
            {
                logger?.LogDebug("Maximum number of running trades has been reached ({MaxRunningTrades})", options.MaxRunningTrades);
                return;
            }

            Dollar quantizedPriceInUsd = Math.Floor(data.LastPrice / options.Factor) * options.Factor;
            var runningTrade = runningTrades.FirstOrDefault(x => x.price == quantizedPriceInUsd);
            if (runningTrade != null)
            {
                logger?.LogDebug("A running trade with the same price already exists ({Price}$)", quantizedPriceInUsd);
                return;
            }

            // Only count running trade margins - open trades will be canceled and their margin freed
            Satoshi isolatedMarginInSats = runningTrades.Select(x => x.margin.Value + x.maintenance_margin.Value).Sum();
            Satoshi availableMarginInSats = user.balance - isolatedMarginInSats;

            Satoshi oneUsdInSats = decimal.ToInt64(Constants.SatoshisPerBitcoin.Value / data.LastPrice);
            if (availableMarginInSats <= oneUsdInSats)
            {
                logger?.LogDebug("No available margin");
                return;
            }

            var openTrades = await client.GetOpenTrades(options.Key, options.Passphrase, options.Secret);
            var openTrade = openTrades.FirstOrDefault(x => x.price == quantizedPriceInUsd);
            if (openTrade != null)
            {
                logger?.LogDebug("An open trade with the same price already exists ({Price}$)", quantizedPriceInUsd);
                return;
            }

            Dollar exitPriceInUsd;
            if (!options.TargetNetPLInSats.HasValue)
            {
                exitPriceInUsd = quantizedPriceInUsd + options.Takeprofit;
            }
            else
            {
                var feeRate = GetFeeRateFromTier(user.fee_tier);
                logger?.LogDebug("User fee tier {FeeTier} mapped to fee rate {FeeRate:P}", user.fee_tier, feeRate);

                Satoshi targetNetPLInSats = options.TargetNetPLInSats.Value;
                Dollar adjustedExitPriceInUsd = TradeFactory.CalculateExitPriceForTargetNetPL(options.Quantity, quantizedPriceInUsd, options.Leverage, feeRate, targetNetPLInSats.Value, TradeSide.Buy);
                Dollar roundedExitPriceInUsd = Math.Ceiling(adjustedExitPriceInUsd * 2) / 2; // Round up to nearest 0.5 for LN Markets compatibility
                logger?.LogDebug("Adjusted exit price to {AdjustedExitPrice}$ for a net P&L of {TargetProfit} sats", roundedExitPriceInUsd, targetNetPLInSats);

                exitPriceInUsd = roundedExitPriceInUsd;
            }

            if (exitPriceInUsd >= options.MaxTakeprofitPrice)
            {
                logger?.LogDebug("Exit price {ExitPrice}$ exceeds maximum take profit price {MaximumPrice}$", exitPriceInUsd, options.MaxTakeprofitPrice);
                return;
            }

            // TODO ceil
            Satoshi requiredMarginInSats = decimal.ToInt64(Constants.SatoshisPerBitcoin.Value / quantizedPriceInUsd * options.Quantity / options.Leverage);
            if (requiredMarginInSats > availableMarginInSats)
            {
                logger?.LogWarning("Insufficient margin: required {RequiredMargin} sats | available {AvailableMargin} sats", requiredMarginInSats, availableMarginInSats);
                return;
            }

            foreach (var oldTrade in openTrades)
            {
                if (!await client.Cancel(options.Key, options.Passphrase, options.Secret, oldTrade.id))
                {
                    logger?.LogWarning("Failed to cancel trade {TradeId}", oldTrade.id);
                }
            }

            if (!await client.CreateLimitBuyOrder(options.Key, options.Passphrase, options.Secret, quantizedPriceInUsd, exitPriceInUsd, options.Leverage, options.Quantity))
            {
                logger?.LogError("Failed to create limit buy order:\n\t[price: {Price}, takeprofit: {TakeProfit}, leverage: {Leverage}, quantity: {Quantity}]", quantizedPriceInUsd, exitPriceInUsd, options.Leverage, options.Quantity);
                return;
            }

            logger?.LogInformation("Successfully created limit buy order:\n\t[price: {Price}, takeprofit: {TakeProfit}, leverage: {Leverage}, quantity: {Quantity}]", quantizedPriceInUsd, exitPriceInUsd, options.Leverage, options.Quantity);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error during trade execution");
        }
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
