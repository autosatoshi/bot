using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using AutoBot.Services;
using FluentAssertions;
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
        _mockApiService.Verify(x => x.AddMarginInSats(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        
        // Assert balance unchanged
        _defaultUser.balance.Should().Be(1000000); // No margin added
        _defaultUser.synthetic_usd_balance.Should().Be(5000m); // No swap performed
    }

    [Fact]
    public async Task ProcessMarginManagement_WithTradeWithinLossLimit_ShouldAddMargin()
    {
        // Arrange
        var runningTrade = TradeFactory.CreateLosingTrade(
            quantityInUsd: 100m,
            entryPriceInUsd: 49000m,
            leverage: 2m,
            side: TradeSide.Buy,
            lossPercentage: -60m, // -60% loss (worse than -50% threshold, so margin should be added)
            TradeState.Running,
            marginInSats: 1000m,
            id: "trade-1");

        var runningTrades = new List<FuturesTradeModel> { runningTrade };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);
        _mockApiService.Setup(x => x.AddMarginInSats(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, It.IsAny<string>(), It.IsAny<long>()))
            .ReturnsAsync(true);
        _mockApiService.Setup(x => x.SwapUsdInBtc(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, It.IsAny<int>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        // oneUsdInSats = 100,000,000 / 50,000 = 2,000 sats per USD
        // oneMarginCallInSats = 2,000 * 10 = 20,000 sats  
        var expectedAddedMargin = 20000;
        _mockApiService.Verify(x => x.AddMarginInSats(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "trade-1", expectedAddedMargin), Times.Once);
        _mockApiService.Verify(x => x.SwapUsdInBtc(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, 10), Times.Once);
        
        // Assert balance updates
        const long expectedFinalBalance = 1000000 - 20000; // 980,000
        const decimal expectedFinalUsdBalance = 5000m;
        _defaultUser.balance.Should().Be(expectedFinalBalance); // Balance should be reduced by margin added
        _defaultUser.synthetic_usd_balance.Should().Be(expectedFinalUsdBalance); // USD balance unchanged (swap happens outside margin management)
    }

    [Fact]
    public async Task ProcessMarginManagement_WithTradeExceedingLossLimit_ShouldNotAddMargin()
    {
        // Arrange
        var runningTrade = TradeFactory.CreateLosingTrade(
            quantityInUsd: 1m,
            entryPriceInUsd: 49000m,
            leverage: 2m,
            side: TradeSide.Buy,
            lossPercentage: -20m, // -20% loss (exceeds -50% limit: -20% > -50%)
            TradeState.Running,
            marginInSats: 1000m,
            id: "trade-1");

        var runningTrades = new List<FuturesTradeModel> { runningTrade };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        // Loss calculation: (-200 / 1000) * 100 = -20%
        // Since -20% > -50% (MaxLossInPercent), the condition loss <= options.MaxLossInPercent is false
        _mockApiService.Verify(x => x.AddMarginInSats(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        
        // Assert balance unchanged
        _defaultUser.balance.Should().Be(1000000); // No margin added
        _defaultUser.synthetic_usd_balance.Should().Be(5000m); // No swap performed
    }

    [Fact]
    public async Task ProcessMarginManagement_WithZeroMargin_ShouldReturn()
    {
        // Arrange
        var runningTrade = TradeFactory.CreateLosingTrade(
            quantityInUsd: 1m,
            entryPriceInUsd: 49000m,
            leverage: 2m,
            side: TradeSide.Buy,
            lossPercentage: -10m,
            TradeState.Running,
            marginInSats: 1000m,
            id: "trade-1");
        
        // Set invalid margin for this test
        runningTrade.margin = 0m;

        var runningTrades = new List<FuturesTradeModel> { runningTrade };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.AddMarginInSats(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        
        // Assert balance unchanged
        _defaultUser.balance.Should().Be(1000000); // No margin added
        _defaultUser.synthetic_usd_balance.Should().Be(5000m); // No swap performed
    }

    [Fact]
    public async Task ProcessMarginManagement_WithNegativeMargin_ShouldReturn()
    {
        // Arrange
        var runningTrade = TradeFactory.CreateLosingTrade(
            quantityInUsd: 1m,
            entryPriceInUsd: 49000m,
            leverage: 2m,
            side: TradeSide.Buy,
            lossPercentage: -10m,
            TradeState.Running,
            marginInSats: 1000m,
            id: "trade-1");
        
        // Set invalid negative margin for this test
        runningTrade.margin = -100m;

        var runningTrades = new List<FuturesTradeModel> { runningTrade };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret), Times.Once);
        _mockApiService.Verify(x => x.AddMarginInSats(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        
        // Assert balance unchanged
        _defaultUser.balance.Should().Be(1000000); // No margin added
        _defaultUser.synthetic_usd_balance.Should().Be(5000m); // No swap performed
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

        var runningTrade = TradeFactory.CreateLosingTrade(
            quantityInUsd: 100m,
            entryPriceInUsd: 49000m,
            leverage: 2m,
            side: TradeSide.Buy,
            lossPercentage: -60m, // -60% loss (worse than -50% threshold, so margin should be added)
            TradeState.Running,
            marginInSats: 1000m,
            id: "trade-1");

        var runningTrades = new List<FuturesTradeModel> { runningTrade };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);
        _mockApiService.Setup(x => x.AddMarginInSats(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, It.IsAny<string>(), It.IsAny<long>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, userWithLowBalance, _mockLogger.Object);

        // Assert
        // oneUsdInSats = 100,000,000 / 50,000 = 2,000 sats per USD
        // oneMarginCallInSats = 2,000 * 10 = 20,000 sats
        const long expectedAddedMargin = 20000;
        const long expectedFinalBalance = 1000000 - 20000; // 980,000
        const decimal expectedFinalUsdBalance = 5m; // USD balance unchanged - no swap due to insufficient balance
        
        _mockApiService.Verify(x => x.AddMarginInSats(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "trade-1", expectedAddedMargin), Times.Once);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        
        // Assert balance updates
        userWithLowBalance.balance.Should().Be(expectedFinalBalance); // Balance should be reduced by margin added
        userWithLowBalance.synthetic_usd_balance.Should().Be(expectedFinalUsdBalance); // USD balance unchanged (no swap due to insufficient balance)
    }

    [Fact]
    public async Task ProcessMarginManagement_WithMultipleTrades_ShouldProcessOnlyBelowMaxLoss()
    {
        const decimal currentPrice = 99000m;

        // Arrange - Create trades using TradeFactory
        var trade1 = TradeFactory.CreateTrade(
            quantityInUsd: 1000m,
            entryPriceInUsd: 100000m,
            leverage: 80m,
            side: TradeSide.Buy,
            currentPriceInUsd: currentPrice,
            TradeState.Running,
            id: "trade-1");
        
        var trade2 = TradeFactory.CreateTrade(
            quantityInUsd: 2000m,
            entryPriceInUsd: 99750m,
            leverage: 95m,
            side: TradeSide.Buy,
            currentPriceInUsd: currentPrice,
            TradeState.Running,
            id: "trade-2");

        var trade3 = TradeFactory.CreateTrade(
            quantityInUsd: 2000m,
            entryPriceInUsd: 99500m,
            leverage: 95m,
            side: TradeSide.Buy,
            currentPriceInUsd: currentPrice,
            TradeState.Running,
            id: "trade-3");

        var priceDataForLossTrades = new LastPriceData
        {
            LastPrice = currentPrice,
            LastTickDirection = "down",
            Time = "1640995200"
        };

        var runningTrades = new List<FuturesTradeModel> { trade1, trade2, trade3 };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);
        _mockApiService.Setup(x => x.AddMarginInSats(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, It.IsAny<string>(), It.IsAny<long>()))
            .ReturnsAsync(true);
        _mockApiService.Setup(x => x.SwapUsdInBtc(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, It.IsAny<int>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, priceDataForLossTrades, _defaultUser, _mockLogger.Object);

        // Assert
        const long expectedMarginPerTrade = 10100;
        const long expectedTotalMarginAdded = expectedMarginPerTrade * 2; // 2 trades
        const long expectedFinalBalance = 1000000 - expectedTotalMarginAdded; // 979,800
        const decimal expectedFinalUsdBalance = 5000m; // USD balance unchanged (swap happens outside margin management)
        
        _mockApiService.Verify(x => x.AddMarginInSats(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "trade-1", expectedMarginPerTrade), Times.Once);
        _mockApiService.Verify(x => x.AddMarginInSats(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "trade-2", expectedMarginPerTrade), Times.Once);
        _mockApiService.Verify(x => x.AddMarginInSats(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "trade-3", It.IsAny<long>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, 20), Times.Once); // 2 trades * 10 USD
        
        // Assert balance updates
        _defaultUser.balance.Should().Be(expectedFinalBalance); // Balance should be reduced by total margin added
        _defaultUser.synthetic_usd_balance.Should().Be(expectedFinalUsdBalance); // USD balance unchanged (swap happens outside margin management)
    }

    [Fact]
    public async Task ProcessMarginManagement_WhenApiCallFails_ShouldReturn()
    {
        // Arrange
        var runningTrade = TradeFactory.CreateLosingTrade(
            quantityInUsd: 1m,
            entryPriceInUsd: 49000m,
            leverage: 1m,
            side: TradeSide.Buy,
            lossPercentage: -20m,
            TradeState.Running,
            marginInSats: 1000m,
            id: "trade-1");

        var runningTrades = new List<FuturesTradeModel> { runningTrade };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ThrowsAsync(new HttpRequestException("API Error"));

        // Act & Assert - Should not throw
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret), Times.Once);
        _mockApiService.Verify(x => x.AddMarginInSats(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        
        // Assert balance unchanged
        _defaultUser.balance.Should().Be(1000000); // No margin added due to API failure
        _defaultUser.synthetic_usd_balance.Should().Be(5000m); // No swap performed
    }

    [Fact]
    public async Task ProcessMarginManagement_WithZeroCalculatedMargin_ShouldNotAddMargin()
    {
        // Arrange - Create scenario where CalculateMarginToAdd returns 0
        var runningTrade = TradeFactory.CreateTrade(
            quantityInUsd: 2m,
            entryPriceInUsd: 50000m, // Same as current price in _defaultPriceData
            leverage: 1m,
            side: TradeSide.Buy,
            currentPriceInUsd: 50000m, // Same as entry price - no P&L
            TradeState.Running,
            id: "trade-1");
        
        // Set high existing margin to ensure maxMargin calculation results in 0 additional margin
        runningTrade.margin = 2000m;

        var runningTrades = new List<FuturesTradeModel> { runningTrade };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert - No margin should be added since calculated margin is 0
        _mockApiService.Verify(x => x.AddMarginInSats(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        
        // Assert balance unchanged
        _defaultUser.balance.Should().Be(1000000); // No margin added
        _defaultUser.synthetic_usd_balance.Should().Be(5000m); // No swap performed
    }

    [Fact]
    public async Task ProcessMarginManagement_WithInsufficientBalanceForAllTrades_ShouldProcessPartially()
    {
        // Arrange - User with balance for only 2 margin calls
        var userWithLimitedBalance = new UserModel
        {
            uid = "test-uid",
            role = "user",
            balance = 45000, // Only enough for 2 margin calls (2 * 20,000 = 40,000)
            username = "testuser",
            synthetic_usd_balance = 5000m
        };

        var trade1 = TradeFactory.CreateLosingTrade(
            quantityInUsd: 100m,
            entryPriceInUsd: 49000m,
            leverage: 2m,
            side: TradeSide.Buy,
            lossPercentage: -60m,
            TradeState.Running,
            marginInSats: 1000m,
            id: "trade-1");

        var trade2 = TradeFactory.CreateLosingTrade(
            quantityInUsd: 100m,
            entryPriceInUsd: 49000m,
            leverage: 2m,
            side: TradeSide.Buy,
            lossPercentage: -60m,
            TradeState.Running,
            marginInSats: 1000m,
            id: "trade-2");

        var trade3 = TradeFactory.CreateLosingTrade(
            quantityInUsd: 100m,
            entryPriceInUsd: 49000m,
            leverage: 2m,
            side: TradeSide.Buy,
            lossPercentage: -60m,
            TradeState.Running,
            marginInSats: 1000m,
            id: "trade-3");

        var runningTrades = new List<FuturesTradeModel> { trade1, trade2, trade3 };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);
        _mockApiService.Setup(x => x.AddMarginInSats(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, It.IsAny<string>(), It.IsAny<long>()))
            .ReturnsAsync(true);
        _mockApiService.Setup(x => x.SwapUsdInBtc(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, It.IsAny<int>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, userWithLimitedBalance, _mockLogger.Object);

        // Assert
        const long expectedMarginPerTrade = 20000;
        const long expectedTotalMarginAdded = expectedMarginPerTrade * 2; // Only 2 trades processed
        const long expectedFinalBalance = 45000 - expectedTotalMarginAdded; // 5,000
        const decimal expectedFinalUsdBalance = 5000m;
        
        _mockApiService.Verify(x => x.AddMarginInSats(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "trade-1", expectedMarginPerTrade), Times.Once);
        _mockApiService.Verify(x => x.AddMarginInSats(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "trade-2", expectedMarginPerTrade), Times.Once);
        _mockApiService.Verify(x => x.AddMarginInSats(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "trade-3", It.IsAny<long>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, 20), Times.Once); // 2 trades * 10 USD
        
        // Assert balance updates
        userWithLimitedBalance.balance.Should().Be(expectedFinalBalance); // Balance reduced by margin for 2 trades
        userWithLimitedBalance.synthetic_usd_balance.Should().Be(expectedFinalUsdBalance); // USD balance unchanged
    }

    [Fact]
    public async Task ProcessMarginManagement_WithAllTradesLeverage1_ShouldReturn()
    {
        // Arrange
        var trade1 = TradeFactory.CreateLosingTrade(
            quantityInUsd: 100m,
            entryPriceInUsd: 49000m,
            leverage: 1m,
            side: TradeSide.Buy,
            lossPercentage: -60m,
            TradeState.Running,
            marginInSats: 1000m,
            id: "trade-1");

        var trade2 = TradeFactory.CreateLosingTrade(
            quantityInUsd: 100m,
            entryPriceInUsd: 49000m,
            leverage: 1m,
            side: TradeSide.Buy,
            lossPercentage: -60m,
            TradeState.Running,
            marginInSats: 1000m,
            id: "trade-2");

        var trade3 = TradeFactory.CreateLosingTrade(
            quantityInUsd: 100m,
            entryPriceInUsd: 49000m,
            leverage: 1m,
            side: TradeSide.Buy,
            lossPercentage: -60m,
            TradeState.Running,
            marginInSats: 1000m,
            id: "trade-3");

        var runningTrades = new List<FuturesTradeModel> { trade1, trade2, trade3 };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockApiService.Verify(x => x.AddMarginInSats(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        
        // Assert balance unchanged
        _defaultUser.balance.Should().Be(1000000); // No margin added - all trades filtered out by leverage check
        _defaultUser.synthetic_usd_balance.Should().Be(5000m); // No swap performed
    }

    [Fact]
    public async Task ProcessMarginManagement_WithMaxMarginExceeded_ShouldSkipFirstProcessSecond()
    {
        // Arrange
        var trade1 = TradeFactory.CreateLosingTrade(
            quantityInUsd: 1m, // Small quantity = low max margin
            entryPriceInUsd: 49000m,
            leverage: 2m,
            side: TradeSide.Buy,
            lossPercentage: -60m,
            TradeState.Running,
            marginInSats: 1000m,
            id: "trade-1");

        var trade2 = TradeFactory.CreateLosingTrade(
            quantityInUsd: 100m, // Large quantity = high max margin
            entryPriceInUsd: 49000m,
            leverage: 2m,
            side: TradeSide.Buy,
            lossPercentage: -60m,
            TradeState.Running,
            marginInSats: 1000m,
            id: "trade-2");

        var runningTrades = new List<FuturesTradeModel> { trade1, trade2 };
        
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);
        _mockApiService.Setup(x => x.AddMarginInSats(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, It.IsAny<string>(), It.IsAny<long>()))
            .ReturnsAsync(true);
        _mockApiService.Setup(x => x.SwapUsdInBtc(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, It.IsAny<int>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessMarginManagement(_mockApiService.Object, _defaultOptions, _defaultPriceData, _defaultUser, _mockLogger.Object);

        // Assert
        const long expectedMarginPerTrade = 20000;
        const long expectedFinalBalance = 1000000 - expectedMarginPerTrade; // Only trade-2 processed
        const decimal expectedFinalUsdBalance = 5000m;
        
        // trade-1: max margin = (100,000,000 / 49,000) * 1 = ~2,041 sats
        // oneMarginCallInSats + trade.margin = 20,000 + 1,000 = 21,000 > 2,041 (exceeds max margin)
        _mockApiService.Verify(x => x.AddMarginInSats(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "trade-1", It.IsAny<long>()), Times.Never);
        
        // trade-2: max margin = (100,000,000 / 49,000) * 100 = ~204,081 sats  
        // oneMarginCallInSats + trade.margin = 20,000 + 1,000 = 21,000 < 204,081 (within max margin)
        _mockApiService.Verify(x => x.AddMarginInSats(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "trade-2", expectedMarginPerTrade), Times.Once);
        
        _mockApiService.Verify(x => x.SwapUsdInBtc(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, 10), Times.Once); // 1 trade * 10 USD
        
        // Assert balance updates
        _defaultUser.balance.Should().Be(expectedFinalBalance); // Balance reduced by margin for 1 trade only
        _defaultUser.synthetic_usd_balance.Should().Be(expectedFinalUsdBalance); // USD balance unchanged
    }
}