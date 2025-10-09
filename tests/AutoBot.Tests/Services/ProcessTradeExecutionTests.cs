using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using AutoBot.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AutoBot.Tests.Services;

public class ProcessTradeExecutionTests
{
    private readonly Mock<ILnMarketsApiService> _mockApiService;
    private readonly Mock<ILogger> _mockLogger;
    private readonly LnMarketsOptions _defaultOptions;
    private readonly UserModel _defaultUser;
    private readonly LastPriceData _defaultPriceData;

    public ProcessTradeExecutionTests()
    {
        _mockApiService = new Mock<ILnMarketsApiService>();
        _mockLogger = new Mock<ILogger>();
        
        _defaultOptions = new LnMarketsOptions
        {
            Key = "test-key",
            Passphrase = "test-passphrase",
            Secret = "test-secret",
            Factor = 1000,
            MaxRunningTrades = 5,
            Takeprofit = 2000,
            MaxTakeprofitPrice = 100000,
            Leverage = 2,
            Quantity = 1
        };

        _defaultUser = new UserModel
        {
            uid = "test-uid",
            role = "user",
            balance = 10000000, // 0.1 BTC in satoshis - should provide sufficient free margin
            username = "testuser",
            synthetic_usd_balance = 5000m,
            fee_tier = 0 // Tier 1: 0.1% fee rate
        };

        _defaultPriceData = new LastPriceData
        {
            LastPrice = 50000m, // Will result in tradePrice = 50000, oneUsdInSats = 2000
            LastTickDirection = "up",
            Time = "1640995200"
        };
    }

    private static async Task CallProcessTradeExecution(
        ILnMarketsApiService apiService,
        LnMarketsOptions options,
        LastPriceData priceData,
        UserModel user,
        ILogger? logger = null)
    {
        var method = typeof(TradeManager).GetMethod("ProcessTradeExecution", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        await (Task)method!.Invoke(null, [apiService, options, priceData, user, logger])!;
    }

    [Fact]
    public async Task ProcessTradeExecution_WithInvalidLastPrice_ShouldReturn()
    {
        // Arrange
        var invalidPriceData = new LastPriceData
        {
            LastPrice = 0m, // Invalid price
            LastTickDirection = "up",
            Time = "1640995200"
        };

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, invalidPriceData, _defaultUser, _mockLogger.Object);

        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithNegativeLastPrice_ShouldReturn()
    {
        // Arrange
        var invalidPriceData = new LastPriceData
        {
            LastPrice = -100m, // Negative price
            LastTickDirection = "up",
            Time = "1640995200"
        };

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, invalidPriceData, _defaultUser, _mockLogger.Object);

        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithExistingOpenTrade_ShouldReturn()
    {
        // Arrange
        var openTrade = TradeFactory.CreateTrade(
            quantityInUsd: 1m,
            entryPriceInUsd: 50000m, // Same as calculated tradePrice
            leverage: 2m,
            side: TradeSide.Buy,
            currentPriceInUsd: 50000m, // No P&L
            TradeState.Open,
            id: "open-trade");

        var emptyRunningTrades = new List<FuturesTradeModel>();
        var openTrades = new List<FuturesTradeModel> { openTrade };

        _mockApiService.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyRunningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(openTrades);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithExistingRunningTrade_ShouldReturn()
    {
        // Arrange
        // LastPrice = 50000, Factor = 1000 → tradePrice = 50000
        var existingTrade = TradeFactory.CreateTrade(
            quantityInUsd: 1m,
            entryPriceInUsd: 50000m, // Same as calculated tradePrice
            leverage: 2m,
            side: TradeSide.Buy,
            currentPriceInUsd: 50000m, // No P&L
            TradeState.Running,
            id: "existing-trade");

        var runningTrades = new List<FuturesTradeModel> { existingTrade };
        _mockApiService.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(runningTrades);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithMaxRunningTradesExceeded_ShouldReturn()
    {
        // Arrange
        var runningTrades = new List<FuturesTradeModel>();
        for (int i = 0; i < 6; i++) // MaxRunningTrades = 5, so 6 > 5
        {
            var entryPrice = 49000m + i * 100; // Different prices to avoid duplicate match
            runningTrades.Add(TradeFactory.CreateTrade(
                quantityInUsd: 1m,
                entryPriceInUsd: entryPrice,
                leverage: 2m,
                side: TradeSide.Buy,
                currentPriceInUsd: entryPrice, // No P&L
                TradeState.Running,
                id: $"trade-{i}"));
        }

        _mockApiService.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(runningTrades);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithInsufficientFreeMargin_ShouldReturn()
    {
        // Arrange
        var userWithLowBalance = new UserModel
        {
            uid = "test-uid",
            role = "user",
            balance = 1000, // Very low balance - should result in insufficient free margin
            username = "testuser",
            synthetic_usd_balance = 5000m
        };

        var emptyRunningTrades = new List<FuturesTradeModel>();
        var emptyOpenTrades = new List<FuturesTradeModel>();

        _mockApiService.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyRunningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyOpenTrades);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, _defaultPriceData, userWithLowBalance, _mockLogger.Object);

        // Assert
        // oneUsdInSats = 100,000,000 / 50000 = 2000
        // freeMargin = userBalance - 0 = 1000 (since no open/running trades)
        // 1000 <= 2000, so should return early
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithInsufficientMarginForSpecificTrade_ShouldReturn()
    {
        // Arrange - User has some margin but not enough for the specific trade
        var userWithMediumBalance = new UserModel
        {
            uid = "test-uid",
            role = "user",
            balance = 30000, // Enough to pass oneUsdInSats check but not enough for the trade
            username = "testuser",
            synthetic_usd_balance = 5000m,
            fee_tier = 0
        };

        var emptyRunningTrades = new List<FuturesTradeModel>();
        var emptyOpenTrades = new List<FuturesTradeModel>();

        _mockApiService.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyRunningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyOpenTrades);

        // Use options that require high margin
        var highQuantityOptions = new LnMarketsOptions
        {
            Key = "test-key",
            Passphrase = "test-passphrase", 
            Secret = "test-secret",
            Factor = 1000,
            MaxRunningTrades = 5,
            Takeprofit = 2000,
            MaxTakeprofitPrice = 100000,
            Leverage = 1, // Low leverage = high margin requirement
            Quantity = 1000 // High quantity = high margin requirement
        };

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, highQuantityOptions, _defaultPriceData, userWithMediumBalance, _mockLogger.Object);

        // Assert
        // oneUsdInSats = 100,000,000 / 50000 = 2000
        // availableMargin = 30000 > 2000 (passes first check)
        // requiredMargin = (100,000,000 / 50000) * 1000 / 1 = 2,000,000
        // 2,000,000 > 30000 (fails second check)
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithSufficientMarginForSpecificTrade_ShouldCreateOrder()
    {
        // Arrange - User has enough margin for the specific trade
        var userWithHighBalance = new UserModel
        {
            uid = "test-uid",
            role = "user", 
            balance = 5000000, // 0.05 BTC in satoshis - enough for high margin trade
            username = "testuser",
            synthetic_usd_balance = 5000m,
            fee_tier = 0
        };

        var emptyRunningTrades = new List<FuturesTradeModel>();
        var emptyOpenTrades = new List<FuturesTradeModel>();

        _mockApiService.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyRunningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyOpenTrades);
        _mockApiService.Setup(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()))
            .ReturnsAsync(true);

        var highQuantityOptions = new LnMarketsOptions
        {
            Key = "test-key",
            Passphrase = "test-passphrase",
            Secret = "test-secret", 
            Factor = 1000,
            MaxRunningTrades = 5,
            Takeprofit = 2000,
            MaxTakeprofitPrice = 100000,
            Leverage = 1, // Low leverage = high margin requirement
            Quantity = 1000 // High quantity = high margin requirement
        };

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, highQuantityOptions, _defaultPriceData, userWithHighBalance, _mockLogger.Object);

        // Assert
        // oneUsdInSats = 100,000,000 / 50000 = 2000
        // availableMargin = 5,000,000 > 2000 (passes first check)
        // requiredMargin = (100,000,000 / 50000) * 1000 / 1 = 2,000,000  
        // 2,000,000 < 5,000,000 (passes second check)
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(
            highQuantityOptions.Key,
            highQuantityOptions.Passphrase,
            highQuantityOptions.Secret,
            50000m, // quantizedPriceInUsd
            52000m, // exitPriceInUsd (50000 + 2000)
            highQuantityOptions.Leverage,
            highQuantityOptions.Quantity), Times.Once);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithTakeprofitExceedsMax_ShouldReturn()
    {
        // Arrange
        var highPriceData = new LastPriceData
        {
            LastPrice = 99000m, // tradePrice + takeprofit = 99000 + 2000 = 101000 > 100000 (MaxTakeprofitPrice)
            LastTickDirection = "up",
            Time = "1640995200"
        };

        var emptyRunningTrades = new List<FuturesTradeModel>();
        var emptyOpenTrades = new List<FuturesTradeModel>();

        _mockApiService.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyRunningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyOpenTrades);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, highPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithValidConditions_ShouldCreateOrder()
    {
        // Arrange
        var emptyRunningTrades = new List<FuturesTradeModel>();
        var emptyOpenTrades = new List<FuturesTradeModel>();

        _mockApiService.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyRunningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyOpenTrades);
        _mockApiService.Setup(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        // tradePrice = 50000, takeprofit = 2000 → takeprofit price = 52000
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(
            _defaultOptions.Key,
            _defaultOptions.Passphrase,
            _defaultOptions.Secret,
            50000m, // tradePrice
            52000m, // tradePrice + takeprofit
            _defaultOptions.Leverage,
            _defaultOptions.Quantity), Times.Once);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithOldTradesToCancel_ShouldCancelAndCreateOrder()
    {
        // Arrange
        var oldOpenTrade = TradeFactory.CreateTrade(
            quantityInUsd: 1m,
            entryPriceInUsd: 48000m, // Different from tradePrice (50000)
            leverage: 2m,
            side: TradeSide.Buy,
            currentPriceInUsd: 48000m, // No P&L
            TradeState.Open,
            id: "old-trade");

        var emptyRunningTrades = new List<FuturesTradeModel>();
        var openTrades = new List<FuturesTradeModel> { oldOpenTrade };

        _mockApiService.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyRunningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(openTrades);
        _mockApiService.Setup(x => x.Cancel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _mockApiService.Setup(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.Cancel(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "old-trade"), Times.Once);
        // tradePrice = 50000, takeprofit = 2000 → takeprofit price = 52000
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(
            _defaultOptions.Key,
            _defaultOptions.Passphrase,
            _defaultOptions.Secret,
            50000m, // tradePrice
            52000m, // tradePrice + takeprofit
            _defaultOptions.Leverage,
            _defaultOptions.Quantity), Times.Once);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithCancelFailure_ShouldCreateOrder()
    {
        // Arrange
        var oldOpenTrade = TradeFactory.CreateTrade(
            quantityInUsd: 1m,
            entryPriceInUsd: 48000m,
            leverage: 2m,
            side: TradeSide.Buy,
            currentPriceInUsd: 48000m, // No P&L
            TradeState.Open,
            id: "failing-trade");

        var emptyRunningTrades = new List<FuturesTradeModel>();
        var openTrades = new List<FuturesTradeModel> { oldOpenTrade };

        _mockApiService.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyRunningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(openTrades);
        _mockApiService.Setup(x => x.Cancel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);
        _mockApiService.Setup(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.Cancel(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "failing-trade"), Times.Once);
        // Should still create the order despite cancel failure
        // tradePrice = 50000, takeprofit = 2000 → takeprofit price = 52000
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(
            _defaultOptions.Key,
            _defaultOptions.Passphrase,
            _defaultOptions.Secret,
            50000m, // tradePrice
            52000m, // tradePrice + takeprofit
            _defaultOptions.Leverage,
            _defaultOptions.Quantity), Times.Once);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithApiFailure_ShouldReturn()
    {
        // Arrange
        _mockApiService.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("API Error"));

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task ProcessTradeExecution_MarginCalculation_ShouldUseQuantizedPrice()
    {
        // Arrange - Create scenario where market price differs from quantized price
        var priceData = new LastPriceData
        {
            LastPrice = 50750m, // Market price that will be quantized down to 50000
            LastTickDirection = "up", 
            Time = "1640995200"
        };

        var userWithLimitedBalance = new UserModel
        {
            uid = "test-uid",
            role = "user",
            balance = 50000, // Limited balance to make margin calculations matter
            username = "testuser",
            synthetic_usd_balance = 5000m,
            fee_tier = 0
        };

        var emptyRunningTrades = new List<FuturesTradeModel>();
        var emptyOpenTrades = new List<FuturesTradeModel>();

        _mockApiService.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyRunningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyOpenTrades);
        _mockApiService.Setup(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, priceData, userWithLimitedBalance, _mockLogger.Object);

        // Assert
        // Verify that CreateLimitBuyOrder was called with quantized price (50000), not market price (50750)
        // quantizedPrice = Math.Floor(50750 / 1000) * 1000 = 50000
        // requiredMargin = (100,000,000 / 50000) * 1 / 2 = 1000 sats
        // availableMargin = 50000 > 1000, so order should be created
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(
            _defaultOptions.Key,
            _defaultOptions.Passphrase, 
            _defaultOptions.Secret,
            50000m, // Should use quantized price for entry, not market price
            52000m, // 50000 + 2000 takeprofit
            _defaultOptions.Leverage,
            _defaultOptions.Quantity), Times.Once);
    }

    [Theory]
    [InlineData(50500, 1000, 50000)] // 50.5 * 1000 floors to 50 * 1000 = 50000
    [InlineData(50999, 1000, 50000)] // 50.999 * 1000 floors to 50 * 1000 = 50000
    [InlineData(51000, 1000, 51000)] // 51 * 1000 = 51000
    [InlineData(49750, 500, 49500)]  // 99.5 * 500 floors to 99 * 500 = 49500
    public async Task ProcessTradeExecution_TradePriceCalculation_ShouldFloorCorrectly(decimal lastPrice, int factor, decimal expectedTradePrice)
    {
        // Arrange
        var priceData = new LastPriceData
        {
            LastPrice = lastPrice,
            LastTickDirection = "up",
            Time = "1640995200"
        };

        var options = new LnMarketsOptions
        {
            Key = "test-key",
            Passphrase = "test-passphrase",
            Secret = "test-secret",
            Factor = factor,
            MaxRunningTrades = 5,
            Takeprofit = 1000,
            MaxTakeprofitPrice = 100000,
            Leverage = 2,
            Quantity = 1
        };

        var runningTradeAtExpectedPrice = TradeFactory.CreateTrade(
            quantityInUsd: 1m,
            entryPriceInUsd: expectedTradePrice, // This should match the calculated tradePrice
            leverage: 2m,
            side: TradeSide.Buy,
            currentPriceInUsd: expectedTradePrice, // No P&L
            TradeState.Running,
            id: "test-trade");

        var runningTrades = new List<FuturesTradeModel> { runningTradeAtExpectedPrice };
        _mockApiService.Setup(x => x.GetRunningTrades(options.Key, options.Passphrase, options.Secret))
            .ReturnsAsync(runningTrades);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, options, priceData, _defaultUser, _mockLogger.Object);

        // Assert
        // If the calculation is correct, the method should find the existing trade and return early
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Theory]
    [InlineData(100, 114200, 115000, 100, TradeSide.Buy, 875, 113070, 87, 86, 609)]
    [InlineData(1000, 110000, 111000, 50, TradeSide.Buy, 18181, 107843, 909, 900, 8190)]
    [InlineData(500, 114347, 100000, 80, TradeSide.Buy, 5465, 112935.5, 437, 500, -62735)]
    [InlineData(672, 97653, 99342, 74, TradeSide.Buy, 9299, 96351, 688, 676, 11699)]
    [InlineData(10, 67213, 74521, 75, TradeSide.Buy, 198, 66330.5, 14, 13, 1459)]
    [InlineData(4357, 87321.5, 101462.5, 2, TradeSide.Buy, 2494803, 58214.5, 4989, 4294, 695410)]
    [InlineData(10000, 103643.5, 98340.5, 9.43, TradeSide.Buy, 1023166, 93706.5, 9648, 10168, -520292)]
    public void TradeFactory_WithRealWorldLnMarketsValues_ShouldPopulateCorrectFuturesTradeModel(
        decimal quantity,
        decimal entryPrice,
        decimal exitPrice,
        decimal leverage,
        TradeSide side,
        decimal expectedMargin,
        decimal expectedLiquidation,
        decimal expectedOpeningFee,
        decimal expectedClosingFee,
        decimal expectedPL)
    {
        // Act
        var trade = TradeFactory.CreateTrade(
            quantityInUsd: quantity,
            entryPriceInUsd: entryPrice,
            leverage: leverage,
            side: side,
            currentPriceInUsd: exitPrice,
            TradeState.Open);

        // Assert
        Assert.Equal(quantity, trade.quantity);
        Assert.Equal(entryPrice, trade.price);
        Assert.Equal(leverage, trade.leverage);
        Assert.Equal(side.ToString().ToLower(), trade.side);
        Assert.Equal(expectedMargin, trade.margin);
        Assert.Equal(expectedPL, trade.pl);
        Assert.Equal(expectedLiquidation, trade.liquidation);
        Assert.Equal(expectedOpeningFee, trade.opening_fee);
        Assert.Equal(expectedClosingFee, trade.closing_fee);
    }
}