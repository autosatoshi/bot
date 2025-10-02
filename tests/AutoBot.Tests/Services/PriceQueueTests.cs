using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using AutoBot.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AutoBot.Tests.Services;

public sealed class PriceQueueTests
{
    private readonly Mock<ITradeManager> _mockTradeManager;
    private readonly Mock<ILogger<PriceQueue>> _mockLogger;
    private readonly Mock<IOptionsMonitor<LnMarketsOptions>> _mockOptionsMonitor;
    private readonly LnMarketsOptions _defaultOptions;

    public PriceQueueTests()
    {
        _mockTradeManager = new Mock<ITradeManager>();
        _mockLogger = new Mock<ILogger<PriceQueue>>();
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

        _mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(_defaultOptions);

        // Setup default successful trade manager call
        _mockTradeManager.Setup(x => x.HandlePriceUpdateAsync(It.IsAny<LastPriceData>()))
            .Returns(Task.CompletedTask);
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

    [Fact]
    public async Task UpdatePrice_WithValidPrice_ShouldCallTradeManager()
    {
        // Arrange
        var tradeManagerSignal = new TaskCompletionSource();

        _mockTradeManager.Setup(x => x.HandlePriceUpdateAsync(It.IsAny<LastPriceData>()))
            .Returns(Task.CompletedTask)
            .Callback(() => tradeManagerSignal.SetResult());

        {
            using var priceQueue = new PriceQueue(_mockTradeManager.Object, _mockOptionsMonitor.Object, _mockLogger.Object);
            var priceData = CreatePriceData(50000m);

            // Act
            priceQueue.UpdatePrice(priceData);

            // Assert
            await Task.Run(() => tradeManagerSignal.Task.Wait(TimeSpan.FromSeconds(1)));
        }

        _mockTradeManager.Verify(x => x.HandlePriceUpdateAsync(It.Is<LastPriceData>(p => p.LastPrice == 50000m)), Times.Once);
    }

    [Fact]
    public async Task UpdatePrice_WithDuplicatePrice_ShouldSkipSecondCall()
    {
        // Arrange
        var tradeManagerCallCount = 0;
        var firstCallSignal = new TaskCompletionSource();

        _mockTradeManager.Setup(x => x.HandlePriceUpdateAsync(It.IsAny<LastPriceData>()))
            .Returns(Task.CompletedTask)
            .Callback(() =>
            {
                tradeManagerCallCount++;
                if (tradeManagerCallCount == 1)
                    firstCallSignal.SetResult();
            });

        {
            using var priceQueue = new PriceQueue(_mockTradeManager.Object, _mockOptionsMonitor.Object, _mockLogger.Object);
            var priceData1 = CreatePriceData(50000m);
            var priceData2 = CreatePriceData(50000m); // Same price

            // Act
            priceQueue.UpdatePrice(priceData1);
            await Task.Run(() => firstCallSignal.Task.Wait(TimeSpan.FromSeconds(1))); // Wait for first processing

            priceQueue.UpdatePrice(priceData2); // This should be skipped due to duplicate price
            // No need to wait - duplicate detection happens synchronously in the queue
        }

        // Assert - TradeManager should only be called once with the first price
        _mockTradeManager.Verify(x => x.HandlePriceUpdateAsync(It.Is<LastPriceData>(p => p.LastPrice == 50000m)), Times.Once);
        _mockTradeManager.Verify(x => x.HandlePriceUpdateAsync(It.IsAny<LastPriceData>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePrice_WithDifferentPrices_ShouldCallTradeManagerForBoth()
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

        var tradeManagerCallCount = 0;
        var secondCallSignal = new TaskCompletionSource();

        _mockTradeManager.Setup(x => x.HandlePriceUpdateAsync(It.IsAny<LastPriceData>()))
            .Returns(Task.CompletedTask)
            .Callback(() =>
            {
                tradeManagerCallCount++;
                if (tradeManagerCallCount >= 2)
                    secondCallSignal.SetResult();
            });

        _mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(fastOptions);

        {
            using var priceQueue = new PriceQueue(_mockTradeManager.Object, _mockOptionsMonitor.Object, _mockLogger.Object);
            var priceData1 = CreatePriceData(50000m);
            var priceData2 = CreatePriceData(51000m); // Different price

            // Act
            priceQueue.UpdatePrice(priceData1);
            priceQueue.UpdatePrice(priceData2);

            await Task.Run(() => secondCallSignal.Task.Wait(TimeSpan.FromSeconds(2)));
        }

        // Assert - TradeManager should be called for both prices
        _mockTradeManager.Verify(x => x.HandlePriceUpdateAsync(It.Is<LastPriceData>(p => p.LastPrice == 50000m)), Times.Once);
        _mockTradeManager.Verify(x => x.HandlePriceUpdateAsync(It.Is<LastPriceData>(p => p.LastPrice == 51000m)), Times.Once);
    }

    [Fact]
    public async Task UpdatePrice_WithMinCallIntervalViolation_ShouldSkipSecondCall()
    {
        // Arrange
        var tradeManagerCallCount = 0;
        var firstCallSignal = new TaskCompletionSource();

        _mockTradeManager.Setup(x => x.HandlePriceUpdateAsync(It.IsAny<LastPriceData>()))
            .Returns(Task.CompletedTask)
            .Callback(() =>
            {
                tradeManagerCallCount++;
                if (tradeManagerCallCount == 1)
                    firstCallSignal.SetResult();
            });

        {
            using var priceQueue = new PriceQueue(_mockTradeManager.Object, _mockOptionsMonitor.Object, _mockLogger.Object);
            var priceData1 = CreatePriceData(50000m);
            var priceData2 = CreatePriceData(51000m); // Different price but within min interval

            // Act
            priceQueue.UpdatePrice(priceData1);
            await Task.Run(() => firstCallSignal.Task.Wait(TimeSpan.FromSeconds(1)));

            priceQueue.UpdatePrice(priceData2); // Should be skipped due to min call interval
            // No need to wait - interval check happens synchronously
        }

        // Assert - TradeManager should only be called once with the first price
        _mockTradeManager.Verify(x => x.HandlePriceUpdateAsync(It.Is<LastPriceData>(p => p.LastPrice == 50000m)), Times.Once);
        _mockTradeManager.Verify(x => x.HandlePriceUpdateAsync(It.IsAny<LastPriceData>()), Times.Once);
    }

    [Fact]
    public void UpdatePrice_WithNullData_ShouldNotCallTradeManager()
    {
        {
            using var priceQueue = new PriceQueue(_mockTradeManager.Object, _mockOptionsMonitor.Object, _mockLogger.Object);

            // Act
            priceQueue.UpdatePrice(default!);
            
            // No need to wait - null data is rejected immediately
        }

        // Assert - TradeManager should not be called
        _mockTradeManager.Verify(x => x.HandlePriceUpdateAsync(It.IsAny<LastPriceData>()), Times.Never);
    }

    [Fact]
    public void UpdatePrice_WithInvalidTimestamp_ShouldNotCallTradeManager()
    {
        {
            using var priceQueue = new PriceQueue(_mockTradeManager.Object, _mockOptionsMonitor.Object, _mockLogger.Object);

            var invalidTimeData = new LastPriceData
            {
                LastPrice = 50000m,
                LastTickDirection = "up",
                Time = null // Invalid timestamp
            };

            // Act
            priceQueue.UpdatePrice(invalidTimeData);
            
            // No need to wait - invalid timestamp is rejected immediately
        }

        // Assert - TradeManager should not be called
        _mockTradeManager.Verify(x => x.HandlePriceUpdateAsync(It.IsAny<LastPriceData>()), Times.Never);
    }

    [Fact]
    public void UpdatePrice_WithOldTimestamp_ShouldNotCallTradeManager()
    {
        {
            using var priceQueue = new PriceQueue(_mockTradeManager.Object, _mockOptionsMonitor.Object, _mockLogger.Object);

            var oldPriceData = new LastPriceData
            {
                LastPrice = 50000m,
                LastTickDirection = "up",
                Time = DateTime.UtcNow.AddSeconds(-(_defaultOptions.MessageTimeoutSeconds + 1)).ToString("O") // Old timestamp
            };

            // Act
            priceQueue.UpdatePrice(oldPriceData);
            
            // No need to wait - old timestamp is rejected immediately
        }

        // Assert - TradeManager should not be called
        _mockTradeManager.Verify(x => x.HandlePriceUpdateAsync(It.IsAny<LastPriceData>()), Times.Never);
    }

    [Fact]
    public async Task UpdatePrice_WithTradeManagerException_ShouldLogErrorAndContinueProcessing()
    {
        // Arrange - Set up first call to fail, second to succeed
        var secondCallSignal = new TaskCompletionSource();

        _mockTradeManager.SetupSequence(x => x.HandlePriceUpdateAsync(It.IsAny<LastPriceData>()))
            .ThrowsAsync(new Exception("Trade Manager Error"))
            .Returns(Task.CompletedTask);

        _mockTradeManager.Setup(x => x.HandlePriceUpdateAsync(It.Is<LastPriceData>(p => p.LastPrice == 51000m)))
            .Returns(Task.CompletedTask)
            .Callback(() => secondCallSignal.SetResult());

        {
            using var priceQueue = new PriceQueue(_mockTradeManager.Object, _mockOptionsMonitor.Object, _mockLogger.Object);
            var priceData1 = CreatePriceData(50000m);
            var priceData2 = CreatePriceData(51000m);

            // Act
            priceQueue.UpdatePrice(priceData1); // Should fail
            priceQueue.UpdatePrice(priceData2); // Should succeed

            await Task.Run(() => secondCallSignal.Task.Wait(TimeSpan.FromSeconds(1)));
        }

        // Assert - Both calls should attempt to reach TradeManager despite first failure
        _mockTradeManager.Verify(x => x.HandlePriceUpdateAsync(It.Is<LastPriceData>(p => p.LastPrice == 50000m)), Times.Once);
        _mockTradeManager.Verify(x => x.HandlePriceUpdateAsync(It.Is<LastPriceData>(p => p.LastPrice == 51000m)), Times.Once);
        _mockTradeManager.Verify(x => x.HandlePriceUpdateAsync(It.IsAny<LastPriceData>()), Times.Exactly(2));
    }
}