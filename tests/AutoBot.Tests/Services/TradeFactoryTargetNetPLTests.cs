using AutoBot.Services;
using FluentAssertions;

namespace AutoBot.Tests.Services;

public class TradeFactoryTargetNetPLTests
{
    [Theory]
    [InlineData(100, 50000, 1, 0.001, TradeSide.Buy, 0, 50100.100)]
    [InlineData(1000, 60000, 2, 0.001, TradeSide.Buy, 0, 60120.120)]
    [InlineData(500, 75000, 5, 0.001, TradeSide.Buy, 0, 75150.150)]
    [InlineData(2000, 45000, 10, 0.001, TradeSide.Buy, 0, 45090.090)]
    [InlineData(100, 50000, 1, 0.001, TradeSide.Buy, 100, 50125.187)]
    [InlineData(100, 50000, 1, 0.001, TradeSide.Buy, 500, 50225.790)]
    [InlineData(100, 50000, 1, 0.001, TradeSide.Buy, 1000, 50352.112)]
    [InlineData(1000, 60000, 2, 0.001, TradeSide.Buy, 200, 60127.342)]
    [InlineData(500, 75000, 5, 0.001, TradeSide.Buy, 300, 75184.016)]
    public void CalculateExitPriceForTargetNetPL_ShouldProduceCorrectExitPrice(
        decimal quantityInUsd, 
        decimal entryPriceInUsd, 
        decimal leverage, 
        decimal feeRate, 
        TradeSide side,
        decimal targetNetPLInSats,
        decimal expectedExitPriceInSats)
    {
        // Act
        var actualExitPrice = TradeFactory.CalculateExitPriceForTargetNetPL(
            quantityInUsd, entryPriceInUsd, leverage, feeRate, targetNetPLInSats, side);
        
        // Assert - Compare truncated to 3 decimal places
        var truncatedActual = Math.Truncate(actualExitPrice * 1000) / 1000;
        truncatedActual.Should().Be(expectedExitPriceInSats,
            $"Expected exit price to be {expectedExitPriceInSats} (truncated to 3 decimals), but got {truncatedActual}. " +
            $"Details: quantity={quantityInUsd}, entry={entryPriceInUsd}, leverage={leverage}, feeRate={feeRate}, targetNetPL={targetNetPLInSats}, side={side}");
    }
}