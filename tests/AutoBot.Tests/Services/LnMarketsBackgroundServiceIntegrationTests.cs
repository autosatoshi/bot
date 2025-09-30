using AutoBot.Models;
using AutoBot.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AutoBot.Tests.Services;

public sealed class LnMarketsBackgroundServiceIntegrationTests : IDisposable
{
    private readonly ServiceCollection _services;
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<ILogger<LnMarketsBackgroundService>> _mockLogger;
    private readonly Mock<ILnMarketsApiService> _mockApiService;
    private readonly Mock<IPriceQueue> _mockPriceQueue;
    private readonly Mock<ITradeManager> _mockTradeManager;
    private readonly LnMarketsOptions _options;

    public LnMarketsBackgroundServiceIntegrationTests()
    {
        _services = new ServiceCollection();
        _mockLogger = new Mock<ILogger<LnMarketsBackgroundService>>();
        _mockApiService = new Mock<ILnMarketsApiService>();
        _mockPriceQueue = new Mock<IPriceQueue>();
        _mockTradeManager = new Mock<ITradeManager>();

        _options = new LnMarketsOptions
        {
            Endpoint = "https://test.endpoint",
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
            Pause = true // Start paused for testing
        };

        // Configure services
        _services.Configure<LnMarketsOptions>(opts =>
        {
            opts.Endpoint = _options.Endpoint;
            opts.Key = _options.Key;
            opts.Passphrase = _options.Passphrase;
            opts.Secret = _options.Secret;
            opts.ReconnectDelaySeconds = _options.ReconnectDelaySeconds;
            opts.WebSocketBufferSize = _options.WebSocketBufferSize;
            opts.MessageTimeoutSeconds = _options.MessageTimeoutSeconds;
            opts.MinCallIntervalSeconds = _options.MinCallIntervalSeconds;
            opts.MaxRunningTrades = _options.MaxRunningTrades;
            opts.Factor = _options.Factor;
            opts.Takeprofit = _options.Takeprofit;
            opts.MaxTakeprofitPrice = _options.MaxTakeprofitPrice;
            opts.Leverage = _options.Leverage;
            opts.Quantity = _options.Quantity;
            opts.AddMarginInUsd = _options.AddMarginInUsd;
            opts.MaxLossInPercent = _options.MaxLossInPercent;
            opts.Pause = _options.Pause;
        });

        _services.AddSingleton(_mockLogger.Object);
        _services.AddSingleton(_mockApiService.Object);
        _services.AddSingleton(_mockPriceQueue.Object);
        _services.AddSingleton(_mockTradeManager.Object);
        _services.AddSingleton<LnMarketsBackgroundService>();

        _serviceProvider = _services.BuildServiceProvider();
    }

    [Fact]
    public void Constructor_WithValidDependencies_ShouldCreateService()
    {
        // Arrange & Act
        var service = _serviceProvider.GetRequiredService<LnMarketsBackgroundService>();

        // Assert
        service.Should().NotBeNull();
        service.Should().BeOfType<LnMarketsBackgroundService>();
    }

    [Fact]
    public async Task Service_WithCancellation_ShouldStopGracefully()
    {
        // Arrange
        var service = _serviceProvider.GetRequiredService<LnMarketsBackgroundService>();
        using var cts = new CancellationTokenSource();

        // Act
        var task = service.StartAsync(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100)); // Cancel quickly

        await task;

        // Assert
        task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void Service_ShouldImplementBackgroundService()
    {
        // Arrange & Act
        var service = _serviceProvider.GetRequiredService<LnMarketsBackgroundService>();

        // Assert
        service.Should().BeAssignableTo<BackgroundService>();
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
            _serviceProvider?.Dispose();
        }
    }
}