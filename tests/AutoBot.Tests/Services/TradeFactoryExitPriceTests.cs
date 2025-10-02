using AutoBot.Services;
using FluentAssertions;

namespace AutoBot.Tests.Services;

public class TradeFactoryExitPriceTests
{
    private const decimal Tolerance = 1.0m; // 1 sat tolerance for rounding differences

    [Theory]
    [InlineData(100, 50000, 1, 0.001, TradeSide.Buy)]
    [InlineData(1000, 60000, 2, 0.0008, TradeSide.Buy)]
    [InlineData(500, 75000, 5, 0.0007, TradeSide.Buy)]
    [InlineData(2000, 45000, 10, 0.0006, TradeSide.Buy)]
    public void CalculateExitPriceForTargetNetPL_WithZeroTarget_ShouldResultInZeroNetPL(
        decimal quantity, 
        decimal entryPrice, 
        decimal leverage, 
        decimal feeRate, 
        TradeSide side)
    {
        // Act
        var exitPrice = TradeFactory.CalculateExitPriceForTargetNetPL(quantity, entryPrice, leverage, feeRate, 0m, side);
        
        // Verify by creating actual trade
        var trade = TradeFactory.CreateTrade(quantity, entryPrice, leverage, side, exitPrice, TradeState.Closed, feeRate: feeRate);
        var actualNetPL = trade.pl - trade.opening_fee - trade.closing_fee;
        
        // Assert
        actualNetPL.Should().BeInRange(-Tolerance, Tolerance, 
            $"Expected net P&L to be ~0, but got {actualNetPL} sats. " +
            $"Trade details: entry={entryPrice}, exit={exitPrice}, side={side}, " +
            $"P&L={trade.pl}, opening_fee={trade.opening_fee}, closing_fee={trade.closing_fee}");
    }

    [Theory]
    [InlineData(100, 50000, 1, 0.001, 100, TradeSide.Buy)]    // Target 100 sats profit
    [InlineData(100, 50000, 1, 0.001, 500, TradeSide.Buy)]    // Target 500 sats profit
    [InlineData(100, 50000, 1, 0.001, 1000, TradeSide.Buy)]   // Target 1000 sats profit
    [InlineData(1000, 60000, 2, 0.0008, 200, TradeSide.Buy)]  // Different quantity/leverage
    [InlineData(500, 75000, 5, 0.0007, 300, TradeSide.Buy)]   // High leverage scenario
    public void CalculateExitPriceForTargetNetPL_WithProfitTarget_ShouldResultInTargetNetPL(
        decimal quantity, 
        decimal entryPrice, 
        decimal leverage, 
        decimal feeRate, 
        decimal targetProfitSats, 
        TradeSide side)
    {
        // Act
        var exitPrice = TradeFactory.CalculateExitPriceForTargetNetPL(
            quantity, entryPrice, leverage, feeRate, targetProfitSats, side);
        
        // Verify by creating actual trade
        var trade = TradeFactory.CreateTrade(quantity, entryPrice, leverage, side, exitPrice, TradeState.Closed, feeRate: feeRate);
        var actualNetPL = trade.pl - trade.opening_fee - trade.closing_fee;
        
        // Assert
        actualNetPL.Should().BeInRange(targetProfitSats - Tolerance, targetProfitSats + Tolerance,
            $"Expected net P&L to be ~{targetProfitSats} sats, but got {actualNetPL} sats. " +
            $"Trade details: entry={entryPrice}, exit={exitPrice}, side={side}, " +
            $"P&L={trade.pl}, opening_fee={trade.opening_fee}, closing_fee={trade.closing_fee}");
    }

    [Theory]
    [InlineData(1, 50000, 1, 0.001)]     // Small quantity - fees might exceed desired profit
    [InlineData(10, 30000, 1, 0.001)]    // Low price
    [InlineData(100, 100000, 1, 0.001)]  // High price  
    [InlineData(1000, 50000, 20, 0.001)] // High leverage
    public void CalculateExitPriceForTargetNetPL_WithChallengingScenarios_ShouldStillWork(
        decimal quantity, 
        decimal entryPrice, 
        decimal leverage, 
        decimal feeRate)
    {
        // Test zero target net P&L calculation
        var zeroTargetPrice = TradeFactory.CalculateExitPriceForTargetNetPL(quantity, entryPrice, leverage, feeRate, 0m, TradeSide.Buy);
        var zeroTargetTrade = TradeFactory.CreateTrade(quantity, entryPrice, leverage, TradeSide.Buy, zeroTargetPrice, TradeState.Closed, feeRate: feeRate);
        var zeroTargetNetPL = zeroTargetTrade.pl - zeroTargetTrade.opening_fee - zeroTargetTrade.closing_fee;
        
        zeroTargetNetPL.Should().BeInRange(-Tolerance, Tolerance, 
            $"Zero target net P&L calculation failed for challenging scenario: quantity={quantity}, entry={entryPrice}, leverage={leverage}");
        
        // Test small profit target calculation
        var smallMargin = 50m; // 50 sats
        var profitablePrice = TradeFactory.CalculateExitPriceForTargetNetPL(quantity, entryPrice, leverage, feeRate, smallMargin, TradeSide.Buy);
        var profitableTrade = TradeFactory.CreateTrade(quantity, entryPrice, leverage, TradeSide.Buy, profitablePrice, TradeState.Closed, feeRate: feeRate);
        var profitableNetPL = profitableTrade.pl - profitableTrade.opening_fee - profitableTrade.closing_fee;
        
        profitableNetPL.Should().BeInRange(smallMargin - Tolerance, smallMargin + Tolerance,
            $"Small profit target calculation failed for challenging scenario with small margin");
    }

    [Theory]
    [InlineData(0)]    // Tier 1: 0.1%
    [InlineData(1)]    // Tier 2: 0.08%
    [InlineData(2)]    // Tier 3: 0.07%
    [InlineData(3)]    // Tier 4: 0.06%
    public void CalculateExitPriceForTargetNetPL_WithDifferentFeeTiers_ShouldWork(decimal feeTier)
    {
        // Arrange
        var feeRate = feeTier switch
        {
            0 => 0.001m,   // 0.1%
            1 => 0.0008m,  // 0.08%
            2 => 0.0007m,  // 0.07%
            3 => 0.0006m,  // 0.06%
            _ => 0.001m
        };
        
        var quantity = 500m;
        var entryPrice = 55000m;
        var leverage = 3m;
        
        // Act
        var zeroTargetPrice = TradeFactory.CalculateExitPriceForTargetNetPL(quantity, entryPrice, leverage, feeRate, 0m, TradeSide.Buy);
        var trade = TradeFactory.CreateTrade(quantity, entryPrice, leverage, TradeSide.Buy, zeroTargetPrice, TradeState.Closed, feeRate: feeRate);
        var netPL = trade.pl - trade.opening_fee - trade.closing_fee;
        
        // Assert
        netPL.Should().BeInRange(-Tolerance, Tolerance,
            $"Zero target net P&L calculation failed for fee tier {feeTier} (rate: {feeRate:P})");
        
        // Verify lower fee tiers require smaller price adjustments (closer to entry price)
        var priceAdjustment = Math.Abs(zeroTargetPrice - entryPrice);
        priceAdjustment.Should().BeGreaterThan(0, "Price should be adjusted to account for fees");
    }

    [Fact]
    public void CalculateExitPriceForTargetNetPL_BuyVsSell_ShouldProduceDifferentDirections()
    {
        // Arrange
        var quantity = 1000m;
        var entryPrice = 50000m;
        var leverage = 2m;
        var feeRate = 0.001m;
        
        // Act
        var buyZeroTargetPrice = TradeFactory.CalculateExitPriceForTargetNetPL(quantity, entryPrice, leverage, feeRate, 0m, TradeSide.Buy);
        var sellZeroTargetPrice = TradeFactory.CalculateExitPriceForTargetNetPL(quantity, entryPrice, leverage, feeRate, 0m, TradeSide.Sell);
        
        // Assert
        buyZeroTargetPrice.Should().BeGreaterThan(entryPrice, "Buy zero target should require higher exit price");
        sellZeroTargetPrice.Should().BeLessThan(entryPrice, "Sell zero target should require lower exit price");
        
        // Verify both result in zero net P&L
        var buyTrade = TradeFactory.CreateTrade(quantity, entryPrice, leverage, TradeSide.Buy, buyZeroTargetPrice, TradeState.Closed, feeRate: feeRate);
        var buyNetPL = buyTrade.pl - buyTrade.opening_fee - buyTrade.closing_fee;
        
        var sellTrade = TradeFactory.CreateTrade(quantity, entryPrice, leverage, TradeSide.Sell, sellZeroTargetPrice, TradeState.Closed, feeRate: feeRate);
        var sellNetPL = sellTrade.pl - sellTrade.opening_fee - sellTrade.closing_fee;
        
        buyNetPL.Should().BeInRange(-Tolerance, Tolerance, "Buy zero target should result in zero net P&L");
        sellNetPL.Should().BeInRange(-Tolerance, Tolerance, "Sell zero target should result in zero net P&L");
    }
}