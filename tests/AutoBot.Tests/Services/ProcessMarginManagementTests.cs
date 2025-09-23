using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using AutoBot.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AutoBot.Tests.Services;

public class ProcessMarginManagementTests
{
    private readonly Mock<ILnMarketsApiService> _mockApiService;
    private readonly Mock<ILogger> _mockLogger;
    private readonly LnMarketsOptions _defaultOptions;
    private readonly UserModel _defaultUser;
    private readonly LastPriceData _defaultPriceData;

    public ProcessMarginManagementTests()
    {
        _mockApiService = new Mock<ILnMarketsApiService>();
        _mockLogger = new Mock<ILogger>();
        
        _defaultOptions = new LnMarketsOptions
        {
            Key = "test-key",
            Passphrase = "test-passphrase",
            Secret = "test-secret",
            MaxLossInPercent = -50,
            AddMarginInUsd = 10m
        };

        _defaultUser = new UserModel
        {
            uid = "test-uid",
            role = "user",
            balance = 1000000, // 1 BTC in satoshis
            username = "testuser",
            synthetic_usd_balance = 5000m
        };

        _defaultPriceData = new LastPriceData
        {
            LastPrice = 50000m,
            LastTickDirection = "up",
            Time = "1640995200"
        };
    }

    private static async Task CallProcessMarginManagement(
        ILnMarketsApiService apiService,
        LnMarketsOptions options,
        LastPriceData priceData,
        UserModel user,
        ILogger? logger = null)
    {
        // Use reflection to call the private static method
        var method = typeof(TradeManager).GetMethod("ProcessMarginManagement", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        await (Task)method!.Invoke(null, [apiService, options, priceData, user, logger])!;
    }

    [Fact]
    public async Task ProcessMarginManagement_WithNoRunningTrades_ShouldNotAddMargin()
    {
        // Arrange
        var emptyTrades = new List<FuturesTradeModel>();
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(emptyTrades);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.AddMargin(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProcessMarginManagement_WithTradeWithinLossLimit_ShouldAddMargin()
    {
        // Arrange
        var runningTrade = new FuturesTradeModel
        {
            id = "trade-1",
            uid = "test-uid",
            type = "futures",
            side = "buy",
            margin = 1000m,
            pl = -600m, // -60% loss (within -50% limit: -60% <= -50%)
            price = 49000m,
            quantity = 1m,
            leverage = 1m,
            liquidation = 45000m,
            stoploss = 0m,
            takeprofit = 51000m,
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

        var runningTrades = new List<FuturesTradeModel> { runningTrade };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);
        _mockApiService.Setup(x => x.AddMargin(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(true);
        _mockApiService.Setup(x => x.SwapUsdInBtc(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, It.IsAny<int>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.AddMargin(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "trade-1", 10408), Times.Once);
        _mockApiService.Verify(x => x.SwapUsdInBtc(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, 10), Times.Once);
    }

    [Fact]
    public async Task ProcessMarginManagement_WithTradeExceedingLossLimit_ShouldNotAddMargin()
    {
        // Arrange
        var runningTrade = new FuturesTradeModel
        {
            id = "trade-1",
            uid = "test-uid",
            type = "futures",
            side = "buy",
            margin = 1000m,
            pl = -200m, // -20% loss (exceeds -50% limit: -20% > -50%)
            price = 49000m,
            quantity = 1m,
            leverage = 1m,
            liquidation = 45000m,
            stoploss = 0m,
            takeprofit = 51000m,
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

        var runningTrades = new List<FuturesTradeModel> { runningTrade };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        // Loss calculation: (-200 / 1000) * 100 = -20%
        // Since -20% > -50% (MaxLossInPercent), the condition loss <= options.MaxLossInPercent is false
        _mockApiService.Verify(x => x.AddMargin(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProcessMarginManagement_WithZeroMargin_ShouldLogWarningAndSkip()
    {
        // Arrange
        var runningTrade = new FuturesTradeModel
        {
            id = "trade-1",
            uid = "test-uid",
            type = "futures",
            side = "buy",
            margin = 0m, // Invalid margin
            pl = -100m,
            price = 49000m,
            quantity = 1m,
            leverage = 1m,
            liquidation = 45000m,
            stoploss = 0m,
            takeprofit = 51000m,
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

        var runningTrades = new List<FuturesTradeModel> { runningTrade };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Skipping trade trade-1 with invalid margin: 0")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        
        _mockApiService.Verify(x => x.AddMargin(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProcessMarginManagement_WithNegativeMargin_ShouldLogWarningAndSkip()
    {
        // Arrange
        var runningTrade = new FuturesTradeModel
        {
            id = "trade-1",
            uid = "test-uid",
            type = "futures",
            side = "buy",
            margin = -100m, // Invalid negative margin
            pl = -100m,
            price = 49000m,
            quantity = 1m,
            leverage = 1m,
            liquidation = 45000m,
            stoploss = 0m,
            takeprofit = 51000m,
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

        var runningTrades = new List<FuturesTradeModel> { runningTrade };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Skipping trade trade-1 with invalid margin: -100")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMarginManagement_WithInsufficientUsdBalance_ShouldNotSwap()
    {
        // Arrange
        var userWithLowBalance = new UserModel
        {
            uid = "test-uid",
            role = "user",
            balance = 1000000,
            username = "testuser",
            synthetic_usd_balance = 5m // Less than AddMarginInUsd (10)
        };

        var runningTrade = new FuturesTradeModel
        {
            id = "trade-1",
            uid = "test-uid",
            type = "futures",
            side = "buy",
            margin = 1000m,
            pl = -600m, // -60% loss (within -50% limit)
            price = 49000m,
            quantity = 1m,
            leverage = 1m,
            liquidation = 45000m,
            stoploss = 0m,
            takeprofit = 51000m,
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

        var runningTrades = new List<FuturesTradeModel> { runningTrade };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);
        _mockApiService.Setup(x => x.AddMargin(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, userWithLowBalance, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.AddMargin(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "trade-1", 10408), Times.Once);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProcessMarginManagement_WithMultipleTrades_ShouldProcessAll()
    {
        // Arrange
        var trade1 = new FuturesTradeModel
        {
            id = "trade-1",
            uid = "test-uid",
            type = "futures",
            side = "buy",
            margin = 1000m,
            pl = -600m, // -60% loss (within -50% limit)
            price = 49000m,
            quantity = 1m,
            leverage = 1m,
            liquidation = 45000m,
            stoploss = 0m,
            takeprofit = 51000m,
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

        var trade2 = new FuturesTradeModel
        {
            id = "trade-2",
            uid = "test-uid",
            type = "futures",
            side = "buy",
            margin = 2000m,
            pl = -1200m, // -60% loss (within -50% limit)
            price = 48000m,
            quantity = 1m,
            leverage = 1m,
            liquidation = 44000m,
            stoploss = 0m,
            takeprofit = 50000m,
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

        var runningTrades = new List<FuturesTradeModel> { trade1, trade2 };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);
        _mockApiService.Setup(x => x.AddMargin(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(true);
        _mockApiService.Setup(x => x.SwapUsdInBtc(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, It.IsAny<int>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.AddMargin(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "trade-1", 10408), Times.Once);
        _mockApiService.Verify(x => x.AddMargin(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "trade-2", 833), Times.Once);
        _mockApiService.Verify(x => x.SwapUsdInBtc(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, 20), Times.Once); // 2 trades * 10 USD
    }

    [Fact]
    public async Task ProcessMarginManagement_WhenApiCallFails_ShouldContinueProcessing()
    {
        // Arrange
        var runningTrade = new FuturesTradeModel
        {
            id = "trade-1",
            uid = "test-uid",
            type = "futures",
            side = "buy",
            margin = 1000m,
            pl = -200m,
            price = 49000m,
            quantity = 1m,
            leverage = 1m,
            liquidation = 45000m,
            stoploss = 0m,
            takeprofit = 51000m,
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

        var runningTrades = new List<FuturesTradeModel> { runningTrade };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ThrowsAsync(new HttpRequestException("API Error"));

        // Act & Assert - Should not throw
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error during margin management")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMarginManagement_WithZeroCalculatedMargin_ShouldNotAddMargin()
    {
        // Arrange - Create scenario where CalculateMarginToAdd returns 0
        var runningTrade = new FuturesTradeModel
        {
            id = "trade-1",
            uid = "test-uid",
            type = "futures",
            side = "buy",
            margin = 2000m, // High existing margin
            pl = -200m,
            price = 50000m, // Same as current price
            quantity = 2m, // High quantity to make maxMargin calculation result in 0 additional margin
            leverage = 1m,
            liquidation = 45000m,
            stoploss = 0m,
            takeprofit = 51000m,
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

        var runningTrades = new List<FuturesTradeModel> { runningTrade };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert - No margin should be added since calculated margin is 0
        _mockApiService.Verify(x => x.AddMargin(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }
}