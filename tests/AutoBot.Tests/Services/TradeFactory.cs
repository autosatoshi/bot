using AutoBot.Models.LnMarkets;

namespace AutoBot.Tests.Services;

public static class TradeFactory
{
    private const decimal SatoshisPerBitcoin = 100_000_000m;

    public static decimal CalculatePLFromActualPrice(decimal quantityInUsd, decimal entryPriceInUsd, decimal currentPriceInUsd)
    {
        return quantityInUsd * (1 / entryPriceInUsd - 1 / currentPriceInUsd) * SatoshisPerBitcoin;
    }

    public static decimal CalculateActualPriceFromPL(decimal quantityInUsd, decimal entryPriceInUsd, decimal plInSats)
    {
        var plNormalized = plInSats / (quantityInUsd * SatoshisPerBitcoin);
        var inverseCurrentPrice = (1 / entryPriceInUsd) - plNormalized;
        return 1 / inverseCurrentPrice;
    }

    private static decimal CalculateLiquidationPrice(
        decimal entryPrice,
        decimal quantity,
        decimal marginInSats,
        string side)
    {
        if (side == "buy")
        {
            var marginNormalized = marginInSats / (quantity * SatoshisPerBitcoin);
            var inverseLiquidationPrice = (1 / entryPrice) + marginNormalized;
            return 1 / inverseLiquidationPrice;
        }
        else
        {
            var marginNormalized = marginInSats / (quantity * SatoshisPerBitcoin);
            var inverseLiquidationPrice = (1 / entryPrice) - marginNormalized;
            return 1 / inverseLiquidationPrice;
        }
    }

    public static FuturesTradeModel CreateTrade(
        decimal quantity,
        decimal entryPrice,
        decimal leverage,
        string side,
        decimal currentPrice,
        string? id = null,
        string uid = "test-uid")
    {
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

        var liquidationPrice = CalculateLiquidationPrice(
            entryPrice, 
            quantity, 
            marginInSats, 
            side);

        return new FuturesTradeModel
        {
            id = id ?? Guid.NewGuid().ToString(),
            uid = uid,
            type = "futures",
            side = side,
            margin = Math.Round(marginInSats, 0),
            pl = Math.Round(pl, 0),
            price = entryPrice,
            quantity = quantity,
            leverage = leverage,
            liquidation = Math.Round(liquidationPrice, 1),
            stoploss = 0m,
            takeprofit = side == "buy" ? entryPrice * 1.1m : entryPrice * 0.9m,
            creation_ts = 1640995200,
            open = false,
            running = true,
            canceled = false,
            closed = false,
            last_update_ts = 1640995200,
            opening_fee = 0m,
            closing_fee = 0m,
            maintenance_margin = Math.Round(maintenanceMarginInSats, 0),
            sum_carry_fees = 0m
        };
    }
}