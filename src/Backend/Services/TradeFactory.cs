using AutoBot.Models.LnMarkets;

namespace AutoBot.Services;

public static class TradeFactory
{
    private const decimal SatoshisPerBitcoin = 100_000_000m;

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

        return quantityInUsd * ((1 / entryPriceInUsd) - (1 / currentPriceInUsd)) * SatoshisPerBitcoin;
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

        if (quantityInUsd * SatoshisPerBitcoin == 0)
        {
            throw new ArgumentException("Quantity multiplied by SatoshisPerBitcoin cannot be zero", nameof(quantityInUsd));
        }

        var plNormalizedInSats = plInSats / (quantityInUsd * SatoshisPerBitcoin);
        var inverseCurrentPriceInUsd = (1 / entryPriceInUsd) - plNormalizedInSats;

        if (inverseCurrentPriceInUsd <= 0)
        {
            throw new ArgumentException($"Invalid calculation resulted in non-positive inverse current price: {inverseCurrentPriceInUsd}");
        }

        return 1 / inverseCurrentPriceInUsd;
    }

    public static decimal CalculateExitPriceForTargetNetPL(decimal quantityInUsd, decimal entryPriceInUsd, decimal leverage, decimal feeRate, decimal targetNetPLSats, TradeSide side)
    {
        if (quantityInUsd <= 0 || entryPriceInUsd <= 0 || leverage <= 0 || feeRate < 0)
        {
            throw new ArgumentOutOfRangeException("All parameters must be positive");
        }

        var minPriceInUsd = side == TradeSide.Buy ? entryPriceInUsd : entryPriceInUsd * 0.5m;
        var maxPriceInUsd = side == TradeSide.Buy ? entryPriceInUsd * 2m : entryPriceInUsd * 1.5m;

        const decimal tolerance = 0.01m;
        const int maxIterations = 100;

        for (int i = 0; i < maxIterations; i++)
        {
            var testPriceInUsd = (minPriceInUsd + maxPriceInUsd) / 2m;
            var trade = CreateTrade(quantityInUsd, entryPriceInUsd, leverage, side, testPriceInUsd, TradeState.Closed, feeRate: feeRate);
            var netPlInSats = trade.pl - trade.opening_fee - trade.closing_fee;

            if (Math.Abs(netPlInSats - targetNetPLSats) < tolerance)
            {
                return testPriceInUsd;
            }

            if (side == TradeSide.Buy)
            {
                if (netPlInSats < targetNetPLSats)
                {
                    minPriceInUsd = testPriceInUsd;
                }
                else
                {
                    maxPriceInUsd = testPriceInUsd;
                }
            }
            else
            {
                if (netPlInSats < targetNetPLSats)
                {
                    maxPriceInUsd = testPriceInUsd;
                }
                else
                {
                    minPriceInUsd = testPriceInUsd;
                }
            }
        }

        return (minPriceInUsd + maxPriceInUsd) / 2m;
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
        var marginInSats = marginInUsd * (SatoshisPerBitcoin / entryPriceInUsd);

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
            margin = Math.Floor(marginInSats),
            pl = RoundPLInSats(plInSats),
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
            closing_fee = Math.Round(closingFeeInSats, 0),
            maintenance_margin = Math.Round(maintenanceMarginInSats, 0),
            sum_carry_fees = 0m,
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
            calculatedMarginInSats = marginInUsd * (SatoshisPerBitcoin / entryPriceInUsd);
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
            margin = Math.Floor(calculatedMarginInSats),
            pl = RoundPLInSats(plInSats),
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
            closing_fee = Math.Round(closingFeeInSats, 0),
            maintenance_margin = Math.Round(maintenanceMarginInSats, 0),
            sum_carry_fees = 0m,
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

        var marginNormalized = marginInSats / (quantityInUsd * SatoshisPerBitcoin);
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

    private static decimal CalculateOpeningFeeInSats(decimal quantityInUsd, decimal entryPriceInUsd, decimal feeRate)
    {
        return Math.Floor((quantityInUsd / entryPriceInUsd) * feeRate * SatoshisPerBitcoin);
    }

    private static decimal CalculateClosingFeeInSats(decimal quantityInUsd, decimal exitPriceInUsd, decimal feeRate)
    {
        return Math.Floor((quantityInUsd / exitPriceInUsd) * feeRate * SatoshisPerBitcoin);
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
