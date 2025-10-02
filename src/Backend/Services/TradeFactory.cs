using AutoBot.Models.LnMarkets;

namespace AutoBot.Services;

public static class TradeFactory
{
    private const decimal SatoshisPerBitcoin = 100_000_000m;

    public static decimal CalculatePLFromActualPrice(decimal quantityInUsd, decimal entryPriceInUsd, decimal currentPriceInUsd)
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

        var plNormalized = plInSats / (quantityInUsd * SatoshisPerBitcoin);
        var inverseCurrentPrice = (1 / entryPriceInUsd) - plNormalized;

        if (inverseCurrentPrice <= 0)
        {
            throw new ArgumentException($"Invalid calculation resulted in non-positive inverse current price: {inverseCurrentPrice}");
        }

        return 1 / inverseCurrentPrice;
    }

    public static decimal CalculateExitPriceForTargetNetPL(decimal quantity, decimal entryPrice, decimal leverage, decimal feeRate, decimal targetNetPLSats, TradeSide side)
    {
        if (quantity <= 0 || entryPrice <= 0 || leverage <= 0 || feeRate < 0)
        {
            throw new ArgumentOutOfRangeException("All parameters must be positive");
        }

        var minPrice = side == TradeSide.Buy ? entryPrice : entryPrice * 0.5m;
        var maxPrice = side == TradeSide.Buy ? entryPrice * 2m : entryPrice * 1.5m;

        const decimal tolerance = 0.01m;
        const int maxIterations = 100;

        for (int i = 0; i < maxIterations; i++)
        {
            var testPrice = (minPrice + maxPrice) / 2m;
            var trade = CreateTrade(quantity, entryPrice, leverage, side, testPrice, TradeState.Closed, feeRate: feeRate);
            var netPL = trade.pl - trade.opening_fee - trade.closing_fee;

            if (Math.Abs(netPL - targetNetPLSats) < tolerance)
            {
                return testPrice;
            }

            if (side == TradeSide.Buy)
            {
                if (netPL < targetNetPLSats)
                {
                    minPrice = testPrice;
                }
                else
                {
                    maxPrice = testPrice;
                }
            }
            else
            {
                if (netPL < targetNetPLSats)
                {
                    maxPrice = testPrice;
                }
                else
                {
                    minPrice = testPrice;
                }
            }
        }

        return (minPrice + maxPrice) / 2m;
    }

    public static FuturesTradeModel CreateTrade(
        decimal quantity,
        decimal entryPrice,
        decimal leverage,
        TradeSide side,
        decimal currentPrice,
        TradeState state,
        string? id = null,
        string uid = "default-uid",
        decimal feeRate = 0.001m)
    {
        if (leverage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(leverage), "Leverage must be greater than 0");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than 0");
        }

        if (entryPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entryPrice), "Entry price must be greater than 0");
        }

        if (currentPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentPrice), "Current price must be greater than 0");
        }

        var marginInUsd = quantity / leverage;
        var marginInSats = marginInUsd * (SatoshisPerBitcoin / entryPrice);

        var maintenanceMarginInSats = marginInSats * 0.05m;

        decimal pl;
        if (side == TradeSide.Buy)
        {
            pl = CalculatePLFromActualPrice(quantity, entryPrice, currentPrice);
        }
        else
        {
            pl = -CalculatePLFromActualPrice(quantity, entryPrice, currentPrice);
        }

        var openingFee = CalculateOpeningFee(quantity, entryPrice, feeRate);
        var closingFee = CalculateClosingFee(quantity, currentPrice, feeRate);

        var liquidationPrice = CalculateLiquidationPrice(
            entryPrice,
            quantity,
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
            pl = RoundPL(pl),
            price = entryPrice,
            quantity = quantity,
            leverage = leverage,
            liquidation = RoundLiquidationPrice(liquidationPrice),
            stoploss = 0m,
            takeprofit = side == TradeSide.Buy ? entryPrice * 1.1m : entryPrice * 0.9m,
            creation_ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            open = tradeFlags.Open,
            running = tradeFlags.Running,
            canceled = tradeFlags.Canceled,
            closed = tradeFlags.Closed,
            last_update_ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            opening_fee = openingFee,
            closing_fee = Math.Round(closingFee, 0),
            maintenance_margin = Math.Round(maintenanceMarginInSats, 0),
            sum_carry_fees = 0m,
        };
    }

    public static FuturesTradeModel CreateLosingTrade(
        decimal quantity,
        decimal entryPrice,
        decimal leverage,
        TradeSide side,
        decimal lossPercentage,
        TradeState state,
        decimal? marginInSats = null,
        string? id = null,
        string uid = "default-uid",
        decimal feeRate = 0.001m)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than 0");
        }

        if (entryPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entryPrice), "Entry price must be greater than 0");
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
            var marginInUsd = quantity / leverage;
            calculatedMarginInSats = marginInUsd * (SatoshisPerBitcoin / entryPrice);
        }

        if (calculatedMarginInSats < 0)
        {
            throw new ArgumentException($"Calculated margin resulted in negative value: {calculatedMarginInSats}", nameof(leverage));
        }

        var pl = (lossPercentage / 100m) * calculatedMarginInSats;

        var maintenanceMarginInSats = calculatedMarginInSats * 0.05m;

        var openingFee = CalculateOpeningFee(quantity, entryPrice, feeRate);
        var closingFee = CalculateClosingFee(quantity, entryPrice, feeRate);

        var liquidationPrice = CalculateLiquidationPrice(
            entryPrice,
            quantity,
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
            pl = RoundPL(pl),
            price = entryPrice,
            quantity = quantity,
            leverage = leverage,
            liquidation = RoundLiquidationPrice(liquidationPrice),
            stoploss = 0m,
            takeprofit = side == TradeSide.Buy ? entryPrice * 1.1m : entryPrice * 0.9m,
            creation_ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            open = tradeFlags.Open,
            running = tradeFlags.Running,
            canceled = tradeFlags.Canceled,
            closed = tradeFlags.Closed,
            last_update_ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            opening_fee = openingFee,
            closing_fee = Math.Round(closingFee, 0),
            maintenance_margin = Math.Round(maintenanceMarginInSats, 0),
            sum_carry_fees = 0m,
        };
    }

    private static decimal CalculateLiquidationPrice(
        decimal entryPrice,
        decimal quantity,
        decimal marginInSats,
        TradeSide side)
    {
        if (entryPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entryPrice), "Entry price must be greater than 0");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than 0");
        }

        if (marginInSats < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(marginInSats), "Margin must be greater than or equal to 0");
        }

        var marginNormalized = marginInSats / (quantity * SatoshisPerBitcoin);
        decimal inverseLiquidationPrice;

        if (side == TradeSide.Buy)
        {
            inverseLiquidationPrice = (1 / entryPrice) + marginNormalized;
        }
        else
        {
            inverseLiquidationPrice = (1 / entryPrice) - marginNormalized;
        }

        if (inverseLiquidationPrice <= 0)
        {
            throw new ArgumentException($"Invalid calculation resulted in non-positive inverse liquidation price: {inverseLiquidationPrice}", nameof(marginInSats));
        }

        return 1 / inverseLiquidationPrice;
    }

    private static decimal CalculateOpeningFee(decimal quantity, decimal entryPrice, decimal feeRate)
    {
        return Math.Floor((quantity / entryPrice) * feeRate * SatoshisPerBitcoin);
    }

    private static decimal CalculateClosingFee(decimal quantity, decimal exitPrice, decimal feeRate)
    {
        return Math.Floor((quantity / exitPrice) * feeRate * SatoshisPerBitcoin);
    }

    private static decimal RoundLiquidationPrice(decimal liquidationPrice)
    {
        return Math.Round(liquidationPrice * 2m, 0) / 2m;
    }

    private static decimal RoundPL(decimal pl)
    {
        return Math.Floor(pl);
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
