using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using AutoBot.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AutoBot.Tests.Services;

public sealed class TradeManagerQueueTests
{
    private readonly Mock<ILnMarketsApiService> _mockApiService;
    private readonly Mock<ILogger<TradeManager>> _mockLogger;
    private readonly Mock<IOptionsMonitor<LnMarketsOptions>> _mockOptionsMonitor;
    private readonly LnMarketsOptions _defaultOptions;
    private readonly UserModel _defaultUser;

    public TradeManagerQueueTests()
    {
        _mockApiService = new Mock<ILnMarketsApiService>();
        _mockLogger = new Mock<ILogger<TradeManager>>();
        _mockOptionsMonitor = new Mock<IOptionsMonitor<LnMarketsOptions>>();

        _defaultOptions = new LnMarketsOptions
        {
            Key = "test-key",
            Passphrase = "test-passphrase",
            Secret = "test-secret",
            Pause = false,
            MessageTimeoutSeconds = 5,
            MinCallIntervalSeconds = 1,
            Factor = 1000,
            MaxRunningTrades = 5,
            Takeprofit = 2000,
            MaxTakeprofitPrice = 100000,
            Leverage = 2,
            Quantity = 1,
            AddMarginInUsd = 10m,
            MaxLossInPercent = -50
        };

        _defaultUser = new UserModel
        {
            uid = "test-uid",
            role = "user",
            balance = 10000000, // 0.1 BTC in satoshis
            username = "testuser",
            synthetic_usd_balance = 5000m
        };

        _mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(_defaultOptions);

        // Setup default successful business action returns
        _mockApiService.Setup(x => x.AddMargin(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(true);
        _mockApiService.Setup(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(true);
        _mockApiService.Setup(x => x.Cancel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _mockApiService.Setup(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()))
            .ReturnsAsync(true);
    }

    private static LastPriceData CreatePriceData(decimal price, string? time = null)
    {
        return new LastPriceData
        {
            LastPrice = price,
            LastTickDirection = "up",
            Time = time ?? DateTime.UtcNow.ToString("O")
        };
    }

    private FuturesTradeModel CreateLosingTrade(string tradeId, decimal price, decimal margin, decimal loss)
    {
        return new FuturesTradeModel
        {
            id = tradeId,
            uid = "test-uid",
            type = "futures",
            side = "buy",
            price = price,
            margin = margin,
            pl = loss, // Negative value indicates loss
            quantity = 1m,
            leverage = 2m,
            liquidation = price * 0.9m,
            stoploss = 0m,
            takeprofit = price * 1.1m,
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
    }

    private FuturesTradeModel CreateOpenTrade(string tradeId, decimal price)
    {
        return new FuturesTradeModel
        {
            id = tradeId,
            uid = "test-uid",
            type = "futures",
            side = "buy",
            price = price,
            margin = 1000m,
            pl = 0m,
            quantity = 1m,
            leverage = 2m,
            liquidation = price * 0.9m,
            stoploss = 0m,
            takeprofit = price * 1.1m,
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
    }

    [Fact]
    public async Task UpdatePrice_WithMarginManagementTrigger_ShouldAddMarginAndSwapUsd()
    {
        // Arrange - Create a losing trade that needs margin added
        var losingTrade = CreateLosingTrade("losing-trade", 50000m, 1000m, -600m); // 60% loss > 50% threshold
        var runningTrades = new List<FuturesTradeModel> { losingTrade };

        var addMarginSignal = new TaskCompletionSource();
        var swapUsdSignal = new TaskCompletionSource();

        _mockApiService.Setup(x => x.GetUser(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(_defaultUser);
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(new List<FuturesTradeModel>());

        _mockApiService.Setup(x => x.AddMargin(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(true)
            .Callback(() => addMarginSignal.SetResult());
        _mockApiService.Setup(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(true)
            .Callback(() => swapUsdSignal.SetResult());

        {
            using var tradeManager = new TradeManager(_mockApiService.Object, _mockOptionsMonitor.Object, _mockLogger.Object);
            var priceData = CreatePriceData(50000m);

            // Act
            tradeManager.UpdatePrice(priceData);

            // Assert - Wait for both business actions
            await Task.Run(() => addMarginSignal.Task.Wait(TimeSpan.FromSeconds(1)));
            await Task.Run(() => swapUsdSignal.Task.Wait(TimeSpan.FromSeconds(1)));
        }

        _mockApiService.Verify(x => x.AddMargin(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "losing-trade", 10000), Times.Once);
        _mockApiService.Verify(x => x.SwapUsdInBtc(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, 10), Times.Once);
    }

    [Fact]
    public async Task UpdatePrice_WithTradeExecutionTrigger_ShouldCancelAndCreateOrder()
    {
        // Arrange - Set up conditions for trade execution
        var oldOpenTrade = CreateOpenTrade("old-trade", 48000m); // Different from trade price (50000)
        var openTrades = new List<FuturesTradeModel> { oldOpenTrade };

        var cancelSignal = new TaskCompletionSource();
        var createOrderSignal = new TaskCompletionSource();

        _mockApiService.Setup(x => x.GetUser(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(_defaultUser);
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(new List<FuturesTradeModel>()); // No running trades
        _mockApiService.Setup(x => x.GetOpenTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(openTrades);

        _mockApiService.Setup(x => x.Cancel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true)
            .Callback(() => cancelSignal.SetResult());
        _mockApiService.Setup(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()))
            .ReturnsAsync(true)
            .Callback(() => createOrderSignal.SetResult());

        {
            using var tradeManager = new TradeManager(_mockApiService.Object, _mockOptionsMonitor.Object, _mockLogger.Object);
            var priceData = CreatePriceData(50000m); // Will result in trade price 50000

            // Act
            tradeManager.UpdatePrice(priceData);

            // Assert - Wait for both business actions
            await Task.Run(() => cancelSignal.Task.Wait(TimeSpan.FromSeconds(1)));
            await Task.Run(() => createOrderSignal.Task.Wait(TimeSpan.FromSeconds(1)));
        }

        _mockApiService.Verify(x => x.Cancel(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "old-trade"), Times.Once);
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, 50000m, 52000m, 2, 1), Times.Once);
    }

    [Fact]
    public async Task UpdatePrice_WithDuplicatePrice_ShouldPreventDuplicateBusinessActions()
    {
        // Arrange - Set up conditions that would trigger business actions
        var losingTrade = CreateLosingTrade("losing-trade", 50000m, 1000m, -600m);
        var runningTrades = new List<FuturesTradeModel> { losingTrade };

        var addMarginCallCount = 0;
        var firstAddMarginSignal = new TaskCompletionSource();

        _mockApiService.Setup(x => x.GetUser(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(_defaultUser);
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(new List<FuturesTradeModel>());

        _mockApiService.Setup(x => x.AddMargin(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(true)
            .Callback(() =>
            {
                addMarginCallCount++;
                if (addMarginCallCount == 1)
                    firstAddMarginSignal.SetResult();
            });

        {
            using var tradeManager = new TradeManager(_mockApiService.Object, _mockOptionsMonitor.Object, _mockLogger.Object);
            var priceData1 = CreatePriceData(50000m);
            var priceData2 = CreatePriceData(50000m); // Same price

            // Act
            tradeManager.UpdatePrice(priceData1);
            await Task.Run(() => firstAddMarginSignal.Task.Wait(TimeSpan.FromSeconds(1))); // Wait for first processing

            tradeManager.UpdatePrice(priceData2); // This should be skipped immediately due to duplicate price
            // No need to wait - duplicate price detection happens immediately in the background loop
        }

        // Assert - Business actions should only happen once, not twice
        _mockApiService.Verify(x => x.AddMargin(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "losing-trade", 10000), Times.Once);
        _mockApiService.Verify(x => x.SwapUsdInBtc(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, 10), Times.Once);
    }

    [Fact]
    public async Task UpdatePrice_WithDifferentPrices_ShouldTriggerBusinessActionsForBoth()
    {
        // Arrange - Use options with no minimum call interval
        var fastOptions = new LnMarketsOptions
        {
            Key = "test-key",
            Passphrase = "test-passphrase",
            Secret = "test-secret",
            Pause = false,
            MessageTimeoutSeconds = 5,
            MinCallIntervalSeconds = 0, // No minimum interval
            Factor = 1000,
            MaxRunningTrades = 5,
            Takeprofit = 2000,
            MaxTakeprofitPrice = 100000,
            Leverage = 2,
            Quantity = 1,
            AddMarginInUsd = 10m,
            MaxLossInPercent = -50
        };

        // Set up trades that will trigger different business actions at different prices
        var oldTrade1 = CreateOpenTrade("old-trade-1", 48000m); // Will be cancelled for 50000 price
        var oldTrade2 = CreateOpenTrade("old-trade-2", 49000m); // Will be cancelled for 51000 price

        var createOrderCallCount = 0;
        var createOrderSignal = new TaskCompletionSource();

        _mockApiService.Setup(x => x.GetUser(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(_defaultUser);
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(new List<FuturesTradeModel>());

        // Return different open trades for different calls to simulate real scenario
        var setupSequence = _mockApiService.SetupSequence(x => x.GetOpenTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(new List<FuturesTradeModel> { oldTrade1 })
            .ReturnsAsync(new List<FuturesTradeModel> { oldTrade2 });

        _mockApiService.Setup(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()))
            .ReturnsAsync(true)
            .Callback(() =>
            {
                createOrderCallCount++;
                if (createOrderCallCount >= 2)
                    createOrderSignal.SetResult();
            });

        _mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(fastOptions);

        {
            using var tradeManager = new TradeManager(_mockApiService.Object, _mockOptionsMonitor.Object, _mockLogger.Object);
            var priceData1 = CreatePriceData(50000m);
            var priceData2 = CreatePriceData(51000m); // Different price

            // Act
            tradeManager.UpdatePrice(priceData1);
            tradeManager.UpdatePrice(priceData2);

            await Task.Run(() => createOrderSignal.Task.Wait(TimeSpan.FromSeconds(2)));
        }

        // Assert - Both prices should trigger business actions
        _mockApiService.Verify(x => x.Cancel(fastOptions.Key, fastOptions.Passphrase, fastOptions.Secret, "old-trade-1"), Times.Once);
        _mockApiService.Verify(x => x.Cancel(fastOptions.Key, fastOptions.Passphrase, fastOptions.Secret, "old-trade-2"), Times.Once);
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(fastOptions.Key, fastOptions.Passphrase, fastOptions.Secret, 50000m, 52000m, 2, 1), Times.Once);
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(fastOptions.Key, fastOptions.Passphrase, fastOptions.Secret, 51000m, 53000m, 2, 1), Times.Once);
    }

    [Fact]
    public async Task UpdatePrice_WithMinCallIntervalViolation_ShouldPreventSecondBusinessActions()
    {
        // Arrange - Set up conditions that would trigger business actions
        var oldTrade1 = CreateOpenTrade("old-trade-1", 48000m);

        var createOrderCallCount = 0;
        var firstCreateOrderSignal = new TaskCompletionSource();

        _mockApiService.Setup(x => x.GetUser(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(_defaultUser);
        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(new List<FuturesTradeModel>());
        _mockApiService.Setup(x => x.GetOpenTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(new List<FuturesTradeModel> { oldTrade1 });

        _mockApiService.Setup(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()))
            .ReturnsAsync(true)
            .Callback(() =>
            {
                createOrderCallCount++;
                if (createOrderCallCount == 1)
                    firstCreateOrderSignal.SetResult();
            });

        {
            using var tradeManager = new TradeManager(_mockApiService.Object, _mockOptionsMonitor.Object, _mockLogger.Object);
            var priceData1 = CreatePriceData(50000m);
            var priceData2 = CreatePriceData(51000m); // Different price but within min interval

            // Act
            tradeManager.UpdatePrice(priceData1);
            await Task.Run(() => firstCreateOrderSignal.Task.Wait(TimeSpan.FromSeconds(1)));

            tradeManager.UpdatePrice(priceData2); // Should be skipped due to min call interval
            // The second call is immediately skipped due to min call interval, so no need to wait
        }

        // Assert - Only first call should trigger business actions
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, 50000m, 52000m, 2, 1), Times.Once);
        _mockApiService.Verify(x => x.Cancel(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "old-trade-1"), Times.Once); // Only for first trade
    }

    [Fact]
    public void UpdatePrice_WithPausedOptions_ShouldPreventBusinessActions()
    {
        // Arrange - Set pause = true and sync on message processing
        var pausedOptions = new LnMarketsOptions
        {
            Key = "test-key",
            Passphrase = "test-passphrase",
            Secret = "test-secret",
            Pause = true, // Paused
            MessageTimeoutSeconds = 5,
            MinCallIntervalSeconds = 1,
            Factor = 1000,
            MaxRunningTrades = 5,
            Takeprofit = 2000,
            MaxTakeprofitPrice = 100000,
            Leverage = 2,
            Quantity = 1,
            AddMarginInUsd = 10m,
            MaxLossInPercent = -50
        };

        _mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(pausedOptions);

        {
            using var tradeManager = new TradeManager(_mockApiService.Object, _mockOptionsMonitor.Object, _mockLogger.Object);
            var priceData = CreatePriceData(50000m);

            // Act
            tradeManager.UpdatePrice(priceData);
        }

        // Assert - No business actions should be triggered due to pause
        _mockApiService.Verify(x => x.GetUser(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockApiService.Verify(x => x.AddMargin(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockApiService.Verify(x => x.Cancel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task UpdatePrice_WithNullUser_ShouldPreventBusinessActions()
    {
        // Arrange - Sync on GetUser call which will return null
        var processedSignal = new TaskCompletionSource();

        _mockApiService.Setup(x => x.GetUser(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(default(UserModel)!)
            .Callback(() => processedSignal.SetResult());

        {
            using var tradeManager = new TradeManager(_mockApiService.Object, _mockOptionsMonitor.Object, _mockLogger.Object);
            var priceData = CreatePriceData(50000m);

            // Act
            tradeManager.UpdatePrice(priceData);

            // Wait for GetUser to be called and return null
            await Task.Run(() => processedSignal.Task.Wait(TimeSpan.FromSeconds(1)));
        }

        // Assert - GetUser called but no business actions triggered due to null user
        _mockApiService.Verify(x => x.GetUser(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _mockApiService.Verify(x => x.AddMargin(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockApiService.Verify(x => x.Cancel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task UpdatePrice_WithZeroBalanceUser_ShouldPreventBusinessActions()
    {
        // Arrange - Sync on GetUser call which will return zero balance user
        var zeroBalanceUser = new UserModel
        {
            uid = "test-uid",
            role = "user",
            balance = 0, // Zero balance
            username = "testuser",
            synthetic_usd_balance = 5000m
        };

        var processedSignal = new TaskCompletionSource();

        _mockApiService.Setup(x => x.GetUser(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(zeroBalanceUser)
            .Callback(() => processedSignal.SetResult());

        {
            using var tradeManager = new TradeManager(_mockApiService.Object, _mockOptionsMonitor.Object, _mockLogger.Object);
            var priceData = CreatePriceData(50000m);

            // Act
            tradeManager.UpdatePrice(priceData);

            // Wait for GetUser to be called and return zero balance user
            await Task.Run(() => processedSignal.Task.Wait(TimeSpan.FromSeconds(1)));
        }

        // Assert - GetUser called but no business actions triggered due to zero balance
        _mockApiService.Verify(x => x.GetUser(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret), Times.Once);
        _mockApiService.Verify(x => x.AddMargin(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockApiService.Verify(x => x.Cancel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public async Task UpdatePrice_WithApiException_ShouldLogErrorAndContinueProcessing()
    {
        // Arrange - Set up first call to fail, second to succeed
        var losingTrade = CreateLosingTrade("losing-trade", 50000m, 1000m, -600m);
        var runningTrades = new List<FuturesTradeModel> { losingTrade };

        var addMarginSignal = new TaskCompletionSource();

        _mockApiService.SetupSequence(x => x.GetUser(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ThrowsAsync(new HttpRequestException("API Error"))
            .ReturnsAsync(_defaultUser);

        _mockApiService.Setup(x => x.GetRunningTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(runningTrades);
        _mockApiService.Setup(x => x.GetOpenTrades(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret))
            .ReturnsAsync(new List<FuturesTradeModel>());

        _mockApiService.Setup(x => x.AddMargin(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(true)
            .Callback(() => addMarginSignal.SetResult());

        {
            using var tradeManager = new TradeManager(_mockApiService.Object, _mockOptionsMonitor.Object, _mockLogger.Object);
            var priceData1 = CreatePriceData(50000m);
            var priceData2 = CreatePriceData(51000m);

            // Act
            tradeManager.UpdatePrice(priceData1); // Should fail
            tradeManager.UpdatePrice(priceData2); // Should succeed

            await Task.Run(() => addMarginSignal.Task.Wait(TimeSpan.FromSeconds(1)));
        }

        // Assert - Second call should still trigger business actions despite first failure
        _mockApiService.Verify(x => x.AddMargin(_defaultOptions.Key, _defaultOptions.Passphrase, _defaultOptions.Secret, "losing-trade", 10000), Times.Once);
    }

    [Fact]
    public void UpdatePrice_WithNullTimestamp_ShouldPreventBusinessActions()
    {
        {
            using var tradeManager = new TradeManager(_mockApiService.Object, _mockOptionsMonitor.Object, _mockLogger.Object);

            // Act
            tradeManager.UpdatePrice(default!);
        }

        _mockApiService.Verify(x => x.GetUser(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockApiService.Verify(x => x.AddMargin(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockApiService.Verify(x => x.Cancel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public void UpdatePrice_WithInvalidTimestamp_ShouldPreventBusinessActions()
    {
        {
            using var tradeManager = new TradeManager(_mockApiService.Object, _mockOptionsMonitor.Object, _mockLogger.Object);

            var invalidTimeData = new LastPriceData
            {
                LastPrice = 50000m,
                LastTickDirection = "up",
                Time = null  // Invalid timestamp
            };

            // Act
            tradeManager.UpdatePrice(invalidTimeData);
        }

        _mockApiService.Verify(x => x.GetUser(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockApiService.Verify(x => x.AddMargin(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockApiService.Verify(x => x.Cancel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public void UpdatePrice_WithOldTimestamp_ShouldPreventBusinessActions()
    {
        {
            using var tradeManager = new TradeManager(_mockApiService.Object, _mockOptionsMonitor.Object, _mockLogger.Object);

            var oldPriceData = new LastPriceData
            {
                LastPrice = 50000m,
                LastTickDirection = "up",
                Time = DateTime.UtcNow.AddSeconds(-(_defaultOptions.MessageTimeoutSeconds + 1)).ToString("O") // Old timestamp
            };

            // Act
            tradeManager.UpdatePrice(oldPriceData);
        }

        _mockApiService.Verify(x => x.GetUser(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockApiService.Verify(x => x.AddMargin(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockApiService.Verify(x => x.SwapUsdInBtc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockApiService.Verify(x => x.Cancel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockApiService.Verify(x => x.CreateLimitBuyOrder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<double>()), Times.Never);
    }

}