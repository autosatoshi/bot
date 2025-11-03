using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using AutoBot.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AutoBot.Tests.Services;

public class GridSystemTests
{
    private readonly Mock<IMarketplaceClient> _mockClient;
    private readonly Mock<ILogger> _mockLogger;
    private readonly UserModel _defaultUser;

    public GridSystemTests()
    {
        _mockClient = new Mock<IMarketplaceClient>();
        _mockLogger = new Mock<ILogger>();
        
        _defaultUser = new UserModel
        {
            uid = "test-uid",
            role = "user",
            balance = 10000000, // 0.1 BTC in satoshis
            username = "testuser",
            synthetic_usd_balance = 5000m,
            fee_tier = 0
        };
    }

    private static async Task CallProcessTradeExecution(
        IMarketplaceClient client,
        LnMarketsOptions options,
        LastPriceData priceData,
        UserModel user,
        ILogger? logger = null)
    {
        var method = typeof(TradeManager).GetMethod("ProcessTradeExecution", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        await (Task)method!.Invoke(null, [client, options, priceData, user, logger])!;
    }

    [Theory]
    [InlineData(50000, 1000, 50000, 52000)] // Exact quantized price
    [InlineData(50750, 1000, 50000, 52000)] // Market price higher than quantized → entry + takeprofit
    [InlineData(49250, 1000, 49000, 51000)] // Market price higher than quantized → entry + takeprofit
    [InlineData(51999, 1000, 51000, 53000)] // Just below next quantization level → entry + takeprofit
    [InlineData(51000, 1000, 51000, 53000)] // Exact quantized price at different level
    public async Task ProcessTradeExecution_WithDifferentPrices_ShouldQuantizeCorrectly(
        decimal marketPrice, 
        int factor, 
        decimal expectedEntryPrice, 
        decimal expectedExitPrice)
    {
        // Arrange
        var options = new LnMarketsOptions
        {
            Key = "test-key",
            Passphrase = "test-passphrase",
            Secret = "test-secret",
            Factor = factor,
            MaxRunningTrades = 10,
            Takeprofit = 2000,
            MaxTakeprofitPrice = 100000,
            Leverage = 2,
            Quantity = 1
        };

        var priceData = new LastPriceData
        {
            LastPrice = marketPrice,
            LastTickDirection = "up",
            Time = "1640995200"
        };

        var emptyRunningTrades = new List<FuturesTradeModel>();
        var emptyOpenTrades = new List<FuturesTradeModel>();

        _mockClient.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyRunningTrades);
        _mockClient.Setup(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyOpenTrades);
        _mockClient.Setup(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessTradeExecution(_mockClient.Object, options, priceData, _defaultUser, _mockLogger.Object);

        // Assert
        _mockClient.Verify(x => x.CreateLimitBuyOrder(
            options.Key,
            options.Passphrase,
            options.Secret,
            expectedEntryPrice, // Entry price should be quantized
            expectedExitPrice,  // Exit price should be market price + takeprofit
            options.Leverage,
            options.Quantity), Times.Once);
    }

    [Theory]
    [InlineData(50000, 1000, 50000)] // Running trade at exact quantized price
    [InlineData(50750, 1000, 50000)] // Running trade at quantized price, different market price
    [InlineData(49500, 1000, 49000)] // Running trade at different quantized level
    public async Task ProcessTradeExecution_WithExistingRunningTrade_ShouldSkip(
        decimal marketPrice,
        int factor,
        decimal existingTradePrice)
    {
        // Arrange
        var options = new LnMarketsOptions
        {
            Key = "test-key",
            Passphrase = "test-passphrase", 
            Secret = "test-secret",
            Factor = factor,
            MaxRunningTrades = 10,
            Takeprofit = 2000,
            MaxTakeprofitPrice = 100000,
            Leverage = 2,
            Quantity = 1
        };

        var priceData = new LastPriceData
        {
            LastPrice = marketPrice,
            LastTickDirection = "up",
            Time = "1640995200"
        };

        var existingTrade = TradeFactory.CreateTrade(
            quantityInUsd: 1m,
            entryPriceInUsd: existingTradePrice,
            leverage: 2m,
            side: TradeSide.Buy,
            currentPriceInUsd: existingTradePrice,
            TradeState.Running,
            id: "existing-trade");

        var runningTrades = new List<FuturesTradeModel> { existingTrade };
        var emptyOpenTrades = new List<FuturesTradeModel>();

        _mockClient.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(runningTrades);
        _mockClient.Setup(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyOpenTrades);

        // Act
        await CallProcessTradeExecution(_mockClient.Object, options, priceData, _defaultUser, _mockLogger.Object);

        // Assert - Should not create any new trade
        _mockClient.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Theory]
    [InlineData(50000, 1000, 50000)] // Open trade at exact quantized price
    [InlineData(50750, 1000, 50000)] // Open trade at quantized price, different market price
    [InlineData(49500, 1000, 49000)] // Open trade at different quantized level
    public async Task ProcessTradeExecution_WithExistingOpenTrade_ShouldSkip(
        decimal marketPrice,
        int factor,
        decimal existingTradePrice)
    {
        // Arrange
        var options = new LnMarketsOptions
        {
            Key = "test-key",
            Passphrase = "test-passphrase",
            Secret = "test-secret", 
            Factor = factor,
            MaxRunningTrades = 10,
            Takeprofit = 2000,
            MaxTakeprofitPrice = 100000,
            Leverage = 2,
            Quantity = 1
        };

        var priceData = new LastPriceData
        {
            LastPrice = marketPrice,
            LastTickDirection = "up", 
            Time = "1640995200"
        };

        var existingTrade = TradeFactory.CreateTrade(
            quantityInUsd: 1m,
            entryPriceInUsd: existingTradePrice,
            leverage: 2m,
            side: TradeSide.Buy,
            currentPriceInUsd: existingTradePrice,
            TradeState.Open,
            id: "existing-open-trade");

        var emptyRunningTrades = new List<FuturesTradeModel>();
        var openTrades = new List<FuturesTradeModel> { existingTrade };

        _mockClient.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyRunningTrades);
        _mockClient.Setup(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(openTrades);

        // Act
        await CallProcessTradeExecution(_mockClient.Object, options, priceData, _defaultUser, _mockLogger.Object);

        // Assert - Should not create any new trade
        _mockClient.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Theory]
    [InlineData(50000, 2000, 3, 1, 1, true)]  // 1 running + 1 open = 2 < 3 max → should create
    [InlineData(50000, 2000, 3, 2, 1, false)] // 2 running + 1 open = 3 = 3 max → should not create
    [InlineData(50000, 2000, 3, 1, 2, false)] // 1 running + 2 open = 3 = 3 max → should not create
    [InlineData(50000, 2000, 3, 3, 0, false)] // 3 running + 0 open = 3 = 3 max → should not create
    [InlineData(50000, 2000, 5, 2, 2, true)]  // 2 running + 2 open = 4 < 5 max → should create
    public async Task ProcessTradeExecution_WithBatching_ShouldRespectTradeCountLimits(
        decimal marketPrice,
        int batchFactor,
        int maxTradesPerBatch,
        int runningTradesInBatch,
        int openTradesInBatch,
        bool shouldCreateTrade)
    {
        // Arrange
        var options = new LnMarketsOptions
        {
            Key = "test-key",
            Passphrase = "test-passphrase",
            Secret = "test-secret",
            Factor = 1000,
            BatchFactor = batchFactor,
            MaxTradesPerBatch = maxTradesPerBatch,
            EnableBatching = true,
            MaxRunningTrades = 10,
            Takeprofit = 2000,
            MaxTakeprofitPrice = 100000,
            Leverage = 2,
            Quantity = 1
        };

        var priceData = new LastPriceData
        {
            LastPrice = marketPrice,
            LastTickDirection = "up",
            Time = "1640995200"
        };

        // Create running trades in the same batch but NOT at the current quantized price (50000)
        var runningTrades = new List<FuturesTradeModel>();
        for (int i = 0; i < runningTradesInBatch; i++)
        {
            var tradePrice = 51000 + (i * 300); // Start at 51000 to avoid collision with 50000
            runningTrades.Add(TradeFactory.CreateTrade(
                quantityInUsd: 1m,
                entryPriceInUsd: tradePrice,
                leverage: 2m,
                side: TradeSide.Buy,
                currentPriceInUsd: tradePrice,
                TradeState.Running,
                id: $"running-trade-{i}"));
        }

        // Create open trades in the same batch but NOT at the current quantized price (50000)
        var openTrades = new List<FuturesTradeModel>();
        for (int i = 0; i < openTradesInBatch; i++)
        {
            var tradePrice = 50500 + (i * 200); // Different area of same batch, avoiding 50000
            openTrades.Add(TradeFactory.CreateTrade(
                quantityInUsd: 1m,
                entryPriceInUsd: tradePrice,
                leverage: 2m,
                side: TradeSide.Buy,
                currentPriceInUsd: tradePrice,
                TradeState.Open,
                id: $"open-trade-{i}"));
        }

        _mockClient.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(runningTrades);
        _mockClient.Setup(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(openTrades);
        _mockClient.Setup(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessTradeExecution(_mockClient.Object, options, priceData, _defaultUser, _mockLogger.Object);

        // Assert
        var expectedTimes = shouldCreateTrade ? Times.Once() : Times.Never();
        _mockClient.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), expectedTimes);
    }

    [Theory]
    [InlineData(50000, 1000, 2, 50000, 1000)] // Required: 1000, Available: 50000 → should create
    [InlineData(50000, 1000, 2, 999, 1000)]  // Required: 1000, Available: 999 → should not create
    [InlineData(75000, 1000, 1, 1333, 1334)] // Required: 1334, Available: 1333 → should not create
    [InlineData(75000, 1000, 1, 1334, 1334)] // Required: 1334, Available: 1334 → should create (equal)
    [InlineData(75000, 1000, 1, 1335, 1334)] // Required: 1334, Available: 1335 → should create
    public async Task ProcessTradeExecution_WithMarginValidation_ShouldCheckRequiredMargin(
        decimal marketPrice,
        int factor,
        int leverage,
        long availableMargin,
        long expectedRequiredMargin)
    {
        // Arrange
        var options = new LnMarketsOptions
        {
            Key = "test-key",
            Passphrase = "test-passphrase",
            Secret = "test-secret",
            Factor = factor,
            MaxRunningTrades = 10,
            Takeprofit = 2000,
            MaxTakeprofitPrice = 100000,
            Leverage = leverage,
            Quantity = 1
        };

        var priceData = new LastPriceData
        {
            LastPrice = marketPrice,
            LastTickDirection = "up",
            Time = "1640995200"
        };

        var user = new UserModel
        {
            uid = "test-uid",
            role = "user",
            balance = availableMargin,
            username = "testuser",
            synthetic_usd_balance = 5000m,
            fee_tier = 0
        };

        var emptyRunningTrades = new List<FuturesTradeModel>();
        var emptyOpenTrades = new List<FuturesTradeModel>();

        _mockClient.Setup(x => x.GetRunningTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyRunningTrades);
        _mockClient.Setup(x => x.GetOpenTrades(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(emptyOpenTrades);
        _mockClient.Setup(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()))
            .ReturnsAsync(true);

        // Act
        await CallProcessTradeExecution(_mockClient.Object, options, priceData, user, _mockLogger.Object);

        // Assert
        bool shouldCreate = availableMargin >= expectedRequiredMargin;
        var expectedTimes = shouldCreate ? Times.Once() : Times.Never();
        _mockClient.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), expectedTimes);
    }
}