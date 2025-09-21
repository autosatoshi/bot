using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using AutoBot.Services;
using FluentAssertions;
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
            synthetic_usd_balance = 5000m
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
    public async Task ProcessTradeExecution_WithInvalidLastPrice_ShouldLogWarningAndReturn()
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

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Invalid last price: 0")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _mockApiService.Verify(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithNegativeLastPrice_ShouldLogWarningAndReturn()
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

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Invalid last price: -100")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithExistingRunningTrade_ShouldReturn()
    {
        // Arrange
        // LastPrice = 50000, Factor = 1000 → tradePrice = 50000
        var existingTrade = new FuturesTradeModel
        {
            id = "existing-trade",
            uid = "test-uid",
            type = "futures",
            side = "buy",
            price = 50000m, // Same as calculated tradePrice
            margin = 1000m,
            pl = 0m,
            quantity = 1m,
            leverage = 2m,
            liquidation = 45000m,
            stoploss = 0m,
            takeprofit = 52000m,
            creation_ts = 1640995200,
            open = false,
            running = true,
            canceled = false,
            closed = false,
            last_update_ts = 1640995200,
            opening_fee = 0m,
            closing_fee = 0m,
            maintenance_margin = 50m,
            sum_carry_fees = 0m
        };

        var runningTrades = new List<FuturesTradeModel> { existingTrade };
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _mockApiService.Verify(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithMaxRunningTradesExceeded_ShouldReturn()
    {
        // Arrange
        var runningTrades = new List<FuturesTradeModel>();
        for (int i = 0; i < 6; i++) // MaxRunningTrades = 5, so 6 > 5
        {
            runningTrades.Add(new FuturesTradeModel
            {
                id = $"trade-{i}",
                uid = "test-uid",
                type = "futures",
                side = "buy",
                price = 49000m + i * 100, // Different prices to avoid duplicate match
                margin = 1000m,
                pl = 0m,
                quantity = 1m,
                leverage = 2m,
                liquidation = 45000m,
                stoploss = 0m,
                takeprofit = 52000m,
                creation_ts = 1640995200,
                open = false,
                running = true,
                canceled = false,
                closed = false,
                last_update_ts = 1640995200,
                opening_fee = 0m,
                closing_fee = 0m,
                maintenance_margin = 50m,
                sum_carry_fees = 0m
            });
        }

        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _mockApiService.Verify(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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

        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(emptyRunningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
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
    public async Task ProcessTradeExecution_WithExistingOpenTrade_ShouldReturn()
    {
        // Arrange
        var openTrade = new FuturesTradeModel
        {
            id = "open-trade",
            uid = "test-uid",
            type = "futures",
            side = "buy",
            price = 50000m, // Same as calculated tradePrice
            margin = 1000m,
            pl = 0m,
            quantity = 1m,
            leverage = 2m,
            liquidation = 45000m,
            stoploss = 0m,
            takeprofit = 52000m,
            creation_ts = 1640995200,
            open = true,
            running = false,
            canceled = false,
            closed = false,
            last_update_ts = 1640995200,
            opening_fee = 0m,
            closing_fee = 0m,
            maintenance_margin = 50m,
            sum_carry_fees = 0m
        };

        var emptyRunningTrades = new List<FuturesTradeModel>();
        var openTrades = new List<FuturesTradeModel> { openTrade };

        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(emptyRunningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(openTrades);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
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

        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(emptyRunningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
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

        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(emptyRunningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
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
        var oldOpenTrade = new FuturesTradeModel
        {
            id = "old-trade",
            uid = "test-uid",
            type = "futures",
            side = "buy",
            price = 48000m, // Different from tradePrice (50000)
            margin = 1000m,
            pl = 0m,
            quantity = 1m,
            leverage = 2m,
            liquidation = 45000m,
            stoploss = 0m,
            takeprofit = 50000m,
            creation_ts = 1640995200,
            open = true,
            running = false,
            canceled = false,
            closed = false,
            last_update_ts = 1640995200,
            opening_fee = 0m,
            closing_fee = 0m,
            maintenance_margin = 50m,
            sum_carry_fees = 0m
        };

        var emptyRunningTrades = new List<FuturesTradeModel>();
        var openTrades = new List<FuturesTradeModel> { oldOpenTrade };

        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(emptyRunningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(openTrades);
        _mockApiService.Setup(x => x.Cancel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _mockApiService.Setup(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.Cancel(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "old-trade"), Times.Once);
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Once);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithCancelFailure_ShouldLogErrorAndContinue()
    {
        // Arrange
        var oldOpenTrade = new FuturesTradeModel
        {
            id = "failing-trade",
            uid = "test-uid",
            type = "futures",
            side = "buy",
            price = 48000m,
            margin = 1000m,
            pl = 0m,
            quantity = 1m,
            leverage = 2m,
            liquidation = 45000m,
            stoploss = 0m,
            takeprofit = 50000m,
            creation_ts = 1640995200,
            open = true,
            running = false,
            canceled = false,
            closed = false,
            last_update_ts = 1640995200,
            opening_fee = 0m,
            closing_fee = 0m,
            maintenance_margin = 50m,
            sum_carry_fees = 0m
        };

        var emptyRunningTrades = new List<FuturesTradeModel>();
        var openTrades = new List<FuturesTradeModel> { oldOpenTrade };

        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(emptyRunningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(openTrades);
        _mockApiService.Setup(x => x.Cancel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("Cancel failed"));
        _mockApiService.Setup(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to cancel trade failing-trade")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Should still create the order despite cancel failure
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Once);
    }

    [Fact]
    public async Task ProcessTradeExecution_WithApiFailure_ShouldLogError()
    {
        // Arrange
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ThrowsAsync(new HttpRequestException("API Error"));

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error during trade execution")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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

        var runningTradeAtExpectedPrice = new FuturesTradeModel
        {
            id = "test-trade",
            uid = "test-uid",
            type = "futures",
            side = "buy",
            price = expectedTradePrice, // This should match the calculated tradePrice
            margin = 1000m,
            pl = 0m,
            quantity = 1m,
            leverage = 2m,
            liquidation = 45000m,
            stoploss = 0m,
            takeprofit = 52000m,
            creation_ts = 1640995200,
            open = false,
            running = true,
            canceled = false,
            closed = false,
            last_update_ts = 1640995200,
            opening_fee = 0m,
            closing_fee = 0m,
            maintenance_margin = 50m,
            sum_carry_fees = 0m
        };

        var runningTrades = new List<FuturesTradeModel> { runningTradeAtExpectedPrice };
        _mockApiService.Setup(x => x.GetRunningTrades(options.Key, options.Passphrase, options.Secret))
            .ReturnsAsync(runningTrades);

        // Act
        await CallProcessTradeExecution(_mockApiService.Object, options, priceData, _defaultUser, _mockLogger.Object);

        // Assert
        // If the calculation is correct, the method should find the existing trade and return early
        _mockApiService.Verify(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _mockApiService.Verify(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}