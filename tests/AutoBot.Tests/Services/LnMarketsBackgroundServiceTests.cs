using AutoBot.Models;
using AutoBot.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AutoBot.Tests.Services;

public sealed class LnMarketsBackgroundServiceTests : IDisposable
{
    private readonly Mock<ITradeManager> _mockTradeManager;
    private readonly Mock<IOptions<LnMarketsOptions>> _mockOptions;
    private readonly Mock<ILogger<LnMarketsBackgroundService>> _mockLogger;
    private readonly LnMarketsOptions _options;
    private readonly LnMarketsBackgroundService _service;

    public LnMarketsBackgroundServiceTests()
    {
        _mockTradeManager = new Mock<ITradeManager>();
        _mockOptions = new Mock<IOptions<LnMarketsOptions>>();
        _mockLogger = new Mock<ILogger<LnMarketsBackgroundService>>();

        _options = new LnMarketsOptions
        {
            Key = "test-key",
            Passphrase = "test-passphrase",
            Secret = "test-secret",
            ReconnectDelaySeconds = 1,
            WebSocketBufferSize = 4096,
            MessageTimeoutSeconds = 5,
            MinCallIntervalSeconds = 10,
            MaxRunningTrades = 10,
            Factor = 1000,
            Takeprofit = 100,
            MaxTakeprofitPrice = 110000,
            Leverage = 1,
            Quantity = 1,
            AddMarginInUsd = 1,
            MaxLossInPercent = -50,
            Pause = false
        };

        _mockOptions.Setup(o => o.Value).Returns(_options);
        _service = new LnMarketsBackgroundService(_mockTradeManager.Object, _mockOptions.Object, _mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var service = new LnMarketsBackgroundService(_mockTradeManager.Object, _mockOptions.Object, _mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullTradeManager_ShouldNotThrow()
    {
        // Arrange & Act
        var service = new LnMarketsBackgroundService(null!, _mockOptions.Object, _mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldNotThrow()
    {
        // Arrange & Act  
        var service = new LnMarketsBackgroundService(_mockTradeManager.Object, _mockOptions.Object, null!);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldNotThrow()
    {
        // Arrange & Act
        var service = new LnMarketsBackgroundService(_mockTradeManager.Object, null!, _mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            _service?.Dispose();
        }
    }
}