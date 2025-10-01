using AutoBot.Models.LnMarkets;

namespace AutoBot.Tests.Services;

public enum TradeState
{
    Running,  // open = false, running = true
    Open,     // open = true, running = false
    Closed,   // open = false, running = false, closed = true
    Canceled  // open = false, running = false, canceled = true
}

public static class TradeFactory
{
    private const decimal SatoshisPerBitcoin = 100_000_000m;

    private static (bool open, bool running, bool canceled, bool closed) GetTradeStateFlags(TradeState state)
    {
        return state switch
        {
            TradeState.Running => (false, true, false, false),
            TradeState.Open => (true, false, false, false),
            TradeState.Closed => (false, false, false, true),
            TradeState.Canceled => (false, false, true, false),
            _ => throw new ArgumentException("Invalid trade state", nameof(state))
        };
    }

    public static decimal CalculatePLFromActualPrice(decimal quantityInUsd, decimal entryPriceInUsd, decimal currentPriceInUsd)
    {
        if (quantityInUsd <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantityInUsd), "Quantity must be greater than 0");
        if (entryPriceInUsd <= 0)
            throw new ArgumentOutOfRangeException(nameof(entryPriceInUsd), "Entry price must be greater than 0");
        if (currentPriceInUsd <= 0)
            throw new ArgumentOutOfRangeException(nameof(currentPriceInUsd), "Current price must be greater than 0");

        return quantityInUsd * (1 / entryPriceInUsd - 1 / currentPriceInUsd) * SatoshisPerBitcoin;
    }

    public static decimal CalculateActualPriceFromPL(decimal quantityInUsd, decimal entryPriceInUsd, decimal plInSats)
    {
        if (quantityInUsd <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantityInUsd), "Quantity must be greater than 0");
        if (entryPriceInUsd <= 0)
            throw new ArgumentOutOfRangeException(nameof(entryPriceInUsd), "Entry price must be greater than 0");
        if (quantityInUsd * SatoshisPerBitcoin == 0)
            throw new ArgumentException("Quantity multiplied by SatoshisPerBitcoin cannot be zero", nameof(quantityInUsd));

        var plNormalized = plInSats / (quantityInUsd * SatoshisPerBitcoin);
        var inverseCurrentPrice = (1 / entryPriceInUsd) - plNormalized;
        
        if (inverseCurrentPrice <= 0)
            throw new ArgumentException($"Invalid calculation resulted in non-positive inverse current price: {inverseCurrentPrice}");
        
        return 1 / inverseCurrentPrice;
    }

    private static decimal CalculateLiquidationPrice(
        decimal entryPrice,
        decimal quantity,
        decimal marginInSats,
        string side)
    {
        if (entryPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(entryPrice), "Entry price must be greater than 0");
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than 0");
        if (marginInSats < 0)
            throw new ArgumentOutOfRangeException(nameof(marginInSats), "Margin must be greater than or equal to 0");

        var marginNormalized = marginInSats / (quantity * SatoshisPerBitcoin);
        decimal inverseLiquidationPrice;

        if (string.Equals(side, "buy", StringComparison.OrdinalIgnoreCase))
            inverseLiquidationPrice = (1 / entryPrice) + marginNormalized;
        else
            inverseLiquidationPrice = (1 / entryPrice) - marginNormalized;

        if (inverseLiquidationPrice <= 0)
            throw new ArgumentException($"Invalid calculation resulted in non-positive inverse liquidation price: {inverseLiquidationPrice}", nameof(marginInSats));

        return 1 / inverseLiquidationPrice;
    }

    public static FuturesTradeModel CreateTrade(
        decimal quantity,
        decimal entryPrice,
        decimal leverage,
        string side,
        decimal currentPrice,
        TradeState state,
        string? id = null,
        string uid = "test-uid")
    {
        if (leverage <= 0)
            throw new ArgumentOutOfRangeException(nameof(leverage), "Leverage must be greater than 0");
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than 0");
        if (entryPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(entryPrice), "Entry price must be greater than 0");
        if (currentPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(currentPrice), "Current price must be greater than 0");
        if (side != "buy" && side != "sell")
            throw new ArgumentException("Side must be 'buy' or 'sell'", nameof(side));

        var marginInUsd = quantity / leverage;
        var marginInSats = marginInUsd * (SatoshisPerBitcoin / entryPrice);

        var maintenanceMarginInSats = marginInSats * 0.05m;

        decimal pl;
        if (side == "buy")
        {
            pl = CalculatePLFromActualPrice(quantity, entryPrice, currentPrice);
        }
        else
        {
            pl = -CalculatePLFromActualPrice(quantity, entryPrice, currentPrice);
        }

        // Calculate fees
        var feeRate = 0.001m; // Default to 0.1% (tier 1)
        var openingFee = CalculateOpeningFee(quantity, entryPrice, feeRate);
        var closingFee = CalculateClosingFee(quantity, currentPrice, feeRate);

        var liquidationPrice = CalculateLiquidationPrice(
            entryPrice, 
            quantity, 
            Math.Floor(marginInSats), // Use floored margin like LN Markets
            side);

        // Set trade state flags based on enum
        var (open, running, canceled, closed) = GetTradeStateFlags(state);

        return new FuturesTradeModel
        {
            id = id ?? Guid.NewGuid().ToString(),
            uid = uid,
            type = "futures",
            side = side,
            margin = Math.Floor(marginInSats),
            pl = RoundPL(pl),
            price = entryPrice,
            quantity = quantity,
            leverage = leverage,
            liquidation = RoundLiquidationPrice(liquidationPrice),
            stoploss = 0m,
            takeprofit = side == "buy" ? entryPrice * 1.1m : entryPrice * 0.9m,
            creation_ts = 1640995200,
            open = open,
            running = running,
            canceled = canceled,
            closed = closed,
            last_update_ts = 1640995200,
            opening_fee = openingFee,
            closing_fee = Math.Round(closingFee, 0),
            maintenance_margin = Math.Round(maintenanceMarginInSats, 0),
            sum_carry_fees = 0m
        };
    }

    public static FuturesTradeModel CreateLosingTrade(
        decimal quantity,
        decimal entryPrice,
        decimal leverage,
        string side,
        decimal lossPercentage,
        TradeState state,
        decimal? marginInSats = null,
        string? id = null,
        string uid = "test-uid")
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than 0");
        if (entryPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(entryPrice), "Entry price must be greater than 0");
        if (leverage <= 0)
            throw new ArgumentOutOfRangeException(nameof(leverage), "Leverage must be greater than 0");
        if (side != "buy" && side != "sell")
            throw new ArgumentException("Side must be 'buy' or 'sell'", nameof(side));
        if (lossPercentage >= 0)
            throw new ArgumentException("Loss percentage must be negative", nameof(lossPercentage));
        if (marginInSats.HasValue && marginInSats.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(marginInSats), "Margin must be greater than or equal to 0");

        // Calculate margin if not provided
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
            throw new ArgumentException($"Calculated margin resulted in negative value: {calculatedMarginInSats}", nameof(leverage));

        var pl = (lossPercentage / 100m) * calculatedMarginInSats;

        var maintenanceMarginInSats = calculatedMarginInSats * 0.05m;

        // Calculate fees
        var feeRate = 0.001m; // Default to 0.1% (tier 1)
        var openingFee = CalculateOpeningFee(quantity, entryPrice, feeRate);
        var closingFee = CalculateClosingFee(quantity, entryPrice, feeRate); // Use entry price for losing trade

        var liquidationPrice = CalculateLiquidationPrice(
            entryPrice, 
            quantity, 
            calculatedMarginInSats, 
            side);

        // Set trade state flags based on enum
        var (open, running, canceled, closed) = GetTradeStateFlags(state);

        return new FuturesTradeModel
        {
            id = id ?? Guid.NewGuid().ToString(),
            uid = uid,
            type = "futures",
            side = side,
            margin = Math.Floor(calculatedMarginInSats),
            pl = RoundPL(pl),
            price = entryPrice,
            quantity = quantity,
            leverage = leverage,
            liquidation = RoundLiquidationPrice(liquidationPrice),
            stoploss = 0m,
            takeprofit = side == "buy" ? entryPrice * 1.1m : entryPrice * 0.9m,
            creation_ts = 1640995200,
            open = open,
            running = running,
            canceled = canceled,
            closed = closed,
            last_update_ts = 1640995200,
            opening_fee = openingFee,
            closing_fee = Math.Round(closingFee, 0),
            maintenance_margin = Math.Round(maintenanceMarginInSats, 0),
            sum_carry_fees = 0m
        };
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
        // LN Markets rounds liquidation prices to the nearest 0.5
        return Math.Round(liquidationPrice * 2m, 0) / 2m;
    }

    private static decimal RoundPL(decimal pl)
    {
        // LN Markets uses Math.Floor (truncation) for all P&L calculations
        return Math.Floor(pl);
    }
}