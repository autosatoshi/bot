using AutoBot.Models.LnMarkets;

namespace AutoBot.Services;

public static class TradeFactory
{
    public static decimal CalculatePLFromActualPriceInSats(decimal quantityInUsd, decimal entryPriceInUsd, decimal currentPriceInUsd)
    {
        if (quantityInUsd <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityInUsd), "Quantity must be greater than 0");
        }

        if (entryPriceInUsd <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entryPriceInUsd), "Entry price must be greater than 0");
        }

        if (currentPriceInUsd <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentPriceInUsd), "Current price must be greater than 0");
        }

        return quantityInUsd * ((1 / entryPriceInUsd) - (1 / currentPriceInUsd)) * Constants.SatoshisPerBitcoin.Value;
    }

    public static decimal CalculateActualPriceFromPL(decimal quantityInUsd, decimal entryPriceInUsd, decimal plInSats)
    {
        if (quantityInUsd <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityInUsd), "Quantity must be greater than 0");
        }

        if (entryPriceInUsd <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entryPriceInUsd), "Entry price must be greater than 0");
        }

        if (quantityInUsd * Constants.SatoshisPerBitcoin.Value == 0)
        {
            throw new ArgumentException("Quantity multiplied by SatoshisPerBitcoin cannot be zero", nameof(quantityInUsd));
        }

        var plNormalizedInSats = plInSats / (quantityInUsd * Constants.SatoshisPerBitcoin.Value);
        var inverseCurrentPriceInUsd = (1 / entryPriceInUsd) - plNormalizedInSats;

        if (inverseCurrentPriceInUsd <= 0)
        {
            throw new ArgumentException($"Invalid calculation resulted in non-positive inverse current price: {inverseCurrentPriceInUsd}");
        }

        return 1 / inverseCurrentPriceInUsd;
    }

    /// <summary>
    /// Calculates the exact exit price needed to achieve a target net P&L after fees using direct algebraic solution.
    ///
    /// MATHEMATICAL IMPLEMENTATION:
    /// This method solves the circular dependency between exit price and closing fees by deriving an exact formula
    /// from the net P&L equation: target = rawPL - openingFee - closingFee
    ///
    /// The challenge: closingFee = (quantity/exitPrice) * feeRate * SATS_PER_BTC depends on the unknown exitPrice
    ///
    /// Solution: Algebraically rearrange the complete equation to solve for exitPrice directly.
    ///
    /// For Buy trades:  exitPrice = (1 + feeRate) / (((1 - feeRate) / entryPrice) - (target / (quantity * SATS_PER_BTC)))
    /// For Sell trades: exitPrice = (1 - feeRate) / (((1 + feeRate) / entryPrice) + (target / (quantity * SATS_PER_BTC)))
    ///
    /// The fees are embedded in the formula:
    /// - Opening fee: (1 ± feeRate) terms in denominator
    /// - Closing fee: (1 ± feeRate) terms in numerator
    /// - Raw P&L: difference between entry and exit price inverses
    ///
    /// Result: Single exact calculation with no approximation needed.
    /// </summary>
    /// <param name="quantityInUsd">The trade size in USD.</param>
    /// <param name="entryPriceInUsd">The entry price in USD per Bitcoin.</param>
    /// <param name="leverage">The leverage multiplier for the trade.</param>
    /// <param name="feeRate">The trading fee rate as a decimal (e.g., 0.001 for 0.1%).</param>
    /// <param name="targetNetPLInSats">The target net profit/loss in satoshis AFTER all fees.</param>
    /// <param name="side">The trade side (Buy or Sell).</param>
    /// <returns>The exact exit price in USD needed to achieve the target net P&L.</returns>
    public static decimal CalculateExitPriceForTargetNetPL(decimal quantityInUsd, decimal entryPriceInUsd, decimal leverage, decimal feeRate, long targetNetPLInSats, TradeSide side)
    {
        if (quantityInUsd <= 0 || entryPriceInUsd <= 0 || leverage <= 0 || feeRate < 0)
        {
            throw new ArgumentOutOfRangeException("All parameters must be positive");
        }

        var targetNormalizedInUsd = targetNetPLInSats / (quantityInUsd * Constants.SatoshisPerBitcoin.Value);
        var entryPriceInverseInUsd = 1m / entryPriceInUsd;

        decimal exitPriceInUsd;
        if (side == TradeSide.Buy)
        {
            var denominatorInUsd = ((1m - feeRate) * entryPriceInverseInUsd) - targetNormalizedInUsd;
            if (denominatorInUsd <= 0)
            {
                throw new ArgumentException("Target net P&L is too high or fees are too large, resulting in invalid exit price calculation");
            }

            exitPriceInUsd = (1m + feeRate) / denominatorInUsd;
        }
        else
        {
            var denominatorInUsd = ((1m + feeRate) * entryPriceInverseInUsd) + targetNormalizedInUsd;
            if (denominatorInUsd <= 0)
            {
                throw new ArgumentException("Target net P&L is too high or fees are too large, resulting in invalid exit price calculation");
            }

            exitPriceInUsd = (1m - feeRate) / denominatorInUsd;
        }

        if (exitPriceInUsd <= 0)
        {
            throw new ArgumentException("Calculated exit price is non-positive, indicating invalid parameters or target");
        }

        return exitPriceInUsd;
    }

    public static FuturesTradeModel CreateTrade(
        decimal quantityInUsd,
        decimal entryPriceInUsd,
        decimal leverage,
        TradeSide side,
        decimal currentPriceInUsd,
        TradeState state,
        string? id = null,
        string uid = "default-uid",
        decimal feeRate = 0.001m)
    {
        if (leverage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(leverage), "Leverage must be greater than 0");
        }

        if (quantityInUsd <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityInUsd), "Quantity must be greater than 0");
        }

        if (entryPriceInUsd <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entryPriceInUsd), "Entry price must be greater than 0");
        }

        if (currentPriceInUsd <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentPriceInUsd), "Current price must be greater than 0");
        }

        var marginInUsd = quantityInUsd / leverage;
        var marginInSats = marginInUsd * (Constants.SatoshisPerBitcoin.Value / entryPriceInUsd);

        var maintenanceMarginInSats = marginInSats * 0.05m;

        decimal plInSats;
        if (side == TradeSide.Buy)
        {
            plInSats = CalculatePLFromActualPriceInSats(quantityInUsd, entryPriceInUsd, currentPriceInUsd);
        }
        else
        {
            plInSats = -CalculatePLFromActualPriceInSats(quantityInUsd, entryPriceInUsd, currentPriceInUsd);
        }

        var openingFeeInSats = CalculateOpeningFeeInSats(quantityInUsd, entryPriceInUsd, feeRate);
        var closingFeeInSats = CalculateClosingFeeInSats(quantityInUsd, currentPriceInUsd, feeRate);

        var liquidationPriceInUsd = CalculateLiquidationPriceInUsd(
            entryPriceInUsd,
            quantityInUsd,
            Math.Floor(marginInSats),
            side);

        var tradeFlags = GetTradeStateFlags(state);

        return new FuturesTradeModel
        {
            id = id ?? Guid.NewGuid().ToString(),
            uid = uid,
            type = "futures",
            side = side.ToString().ToLower(),
            margin = decimal.ToInt64(Math.Floor(marginInSats)), // TODO
            pl = decimal.ToInt64(RoundPLInSats(plInSats)), // TODO
            price = entryPriceInUsd,
            quantity = quantityInUsd,
            leverage = leverage,
            liquidation = RoundLiquidationPriceInUsd(liquidationPriceInUsd),
            stoploss = 0m,
            takeprofit = side == TradeSide.Buy ? entryPriceInUsd * 1.1m : entryPriceInUsd * 0.9m,
            creation_ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            open = tradeFlags.Open,
            running = tradeFlags.Running,
            canceled = tradeFlags.Canceled,
            closed = tradeFlags.Closed,
            last_update_ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            opening_fee = openingFeeInSats,
            closing_fee = closingFeeInSats, // TODO
            maintenance_margin = decimal.ToInt64(Math.Round(maintenanceMarginInSats, 0)), // TODO
            sum_carry_fees = 0L,
        };
    }

    public static FuturesTradeModel CreateLosingTrade(
        decimal quantityInUsd,
        decimal entryPriceInUsd,
        decimal leverage,
        TradeSide side,
        decimal lossPercentage,
        TradeState state,
        decimal? marginInSats = null,
        string? id = null,
        string uid = "default-uid",
        decimal feeRate = 0.001m)
    {
        if (quantityInUsd <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityInUsd), "Quantity must be greater than 0");
        }

        if (entryPriceInUsd <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entryPriceInUsd), "Entry price must be greater than 0");
        }

        if (leverage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(leverage), "Leverage must be greater than 0");
        }

        if (lossPercentage >= 0)
        {
            throw new ArgumentException("Loss percentage must be negative", nameof(lossPercentage));
        }

        if (marginInSats.HasValue && marginInSats.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(marginInSats), "Margin must be greater than or equal to 0");
        }

        decimal calculatedMarginInSats;
        if (marginInSats.HasValue)
        {
            calculatedMarginInSats = marginInSats.Value;
        }
        else
        {
            var marginInUsd = quantityInUsd / leverage;
            calculatedMarginInSats = marginInUsd * (Constants.SatoshisPerBitcoin.Value / entryPriceInUsd);
        }

        if (calculatedMarginInSats < 0)
        {
            throw new ArgumentException($"Calculated margin resulted in negative value: {calculatedMarginInSats}", nameof(leverage));
        }

        var plInSats = (lossPercentage / 100m) * calculatedMarginInSats;

        var maintenanceMarginInSats = calculatedMarginInSats * 0.05m;

        var openingFeeInSats = CalculateOpeningFeeInSats(quantityInUsd, entryPriceInUsd, feeRate);
        var closingFeeInSats = CalculateClosingFeeInSats(quantityInUsd, entryPriceInUsd, feeRate);

        var liquidationPriceInUsd = CalculateLiquidationPriceInUsd(
            entryPriceInUsd,
            quantityInUsd,
            calculatedMarginInSats,
            side);

        var tradeFlags = GetTradeStateFlags(state);

        return new FuturesTradeModel
        {
            id = id ?? Guid.NewGuid().ToString(),
            uid = uid,
            type = "futures",
            side = side.ToString().ToLower(),
            margin = decimal.ToInt64(Math.Floor(calculatedMarginInSats)), // TODO
            pl = decimal.ToInt64(RoundPLInSats(plInSats)), // TODO
            price = entryPriceInUsd,
            quantity = quantityInUsd,
            leverage = leverage,
            liquidation = RoundLiquidationPriceInUsd(liquidationPriceInUsd),
            stoploss = 0m,
            takeprofit = side == TradeSide.Buy ? entryPriceInUsd * 1.1m : entryPriceInUsd * 0.9m,
            creation_ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            open = tradeFlags.Open,
            running = tradeFlags.Running,
            canceled = tradeFlags.Canceled,
            closed = tradeFlags.Closed,
            last_update_ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            opening_fee = openingFeeInSats,
            closing_fee = closingFeeInSats,
            maintenance_margin = decimal.ToInt64(Math.Round(maintenanceMarginInSats, 0)),
            sum_carry_fees = 0L,
        };
    }

    private static decimal CalculateLiquidationPriceInUsd(
        decimal entryPriceInUsd,
        decimal quantityInUsd,
        decimal marginInSats,
        TradeSide side)
    {
        if (entryPriceInUsd <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entryPriceInUsd), "Entry price must be greater than 0");
        }

        if (quantityInUsd <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityInUsd), "Quantity must be greater than 0");
        }

        if (marginInSats < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(marginInSats), "Margin must be greater than or equal to 0");
        }

        var marginNormalized = marginInSats / (quantityInUsd * Constants.SatoshisPerBitcoin.Value);
        decimal inverseLiquidationPrice;

        if (side == TradeSide.Buy)
        {
            inverseLiquidationPrice = (1 / entryPriceInUsd) + marginNormalized;
        }
        else
        {
            inverseLiquidationPrice = (1 / entryPriceInUsd) - marginNormalized;
        }

        if (inverseLiquidationPrice <= 0)
        {
            throw new ArgumentException($"Invalid calculation resulted in non-positive inverse liquidation price: {inverseLiquidationPrice}", nameof(marginInSats));
        }

        return 1 / inverseLiquidationPrice;
    }

    private static long CalculateOpeningFeeInSats(decimal quantityInUsd, decimal entryPriceInUsd, decimal feeRate)
    {
        return decimal.ToInt64(Math.Floor((quantityInUsd / entryPriceInUsd) * feeRate * Constants.SatoshisPerBitcoin.Value));
    }

    private static long CalculateClosingFeeInSats(decimal quantityInUsd, decimal exitPriceInUsd, decimal feeRate)
    {
        return decimal.ToInt64(Math.Floor((quantityInUsd / exitPriceInUsd) * feeRate * Constants.SatoshisPerBitcoin.Value));
    }

    private static decimal RoundLiquidationPriceInUsd(decimal liquidationPriceInUsd)
    {
        return Math.Round(liquidationPriceInUsd * 2m, 0) / 2m;
    }

    private static decimal RoundPLInSats(decimal plInSats)
    {
        return Math.Floor(plInSats);
    }

    private static (bool Open, bool Running, bool Canceled, bool Closed) GetTradeStateFlags(TradeState state)
    {
        return state switch
        {
            TradeState.Running => (false, true, false, false),
            TradeState.Open => (true, false, false, false),
            TradeState.Closed => (false, false, false, true),
            TradeState.Canceled => (false, false, true, false),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
    }
}
