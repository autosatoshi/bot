using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using AutoBot.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;

namespace AutoBot.Tests.Services;

public sealed class LnMarketsApiServiceRefactoredTests : IDisposable
{
    private readonly Mock<ILogger<LnMarketsApiService>> _mockLogger;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<IOptions<LnMarketsOptions>> _mockOptions;
    private readonly LnMarketsApiService _service;

    public LnMarketsApiServiceRefactoredTests()
    {
        _mockLogger = new Mock<ILogger<LnMarketsApiService>>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
        
        // Create mock options with default values
        _mockOptions = new Mock<IOptions<LnMarketsOptions>>();
        _mockOptions.Setup(o => o.Value).Returns(new LnMarketsOptions());
        
        _service = new LnMarketsApiService(mockFactory.Object, _mockLogger.Object, _mockOptions.Object);
    }

    [Fact]
    public async Task Cancel_WhenSuccessful_ShouldReturnTrue()
    {
        // Arrange
        var key = "test-key";
        var passphrase = "test-passphrase";
        var secret = "test-secret";
        var id = "test-id";

        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"success\": true}", Encoding.UTF8, "application/json")
        };

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.Cancel(key, passphrase, secret, id);

        // Assert
        result.Should().BeTrue();
        
        _mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => 
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains("/v2/futures/cancel")),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task CreateLimitBuyOrder_WhenSuccessful_ShouldReturnTrue()
    {
        // Arrange
        var key = "test-key";
        var passphrase = "test-passphrase";
        var secret = "test-secret";
        var price = 50000m;
        var takeprofit = 51000m;
        var leverage = 2;
        var quantity = 1.5;

        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"success\": true}", Encoding.UTF8, "application/json")
        };

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.CreateLimitBuyOrder(key, passphrase, secret, price, takeprofit, leverage, quantity);

        // Assert
        result.Should().BeTrue();
        
        _mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => 
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains("/v2/futures")),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task FuturesGetRunningTradesAsync_WhenSuccessful_ShouldReturnTrades()
    {
        // Arrange
        var key = "test-key";
        var passphrase = "test-passphrase";
        var secret = "test-secret";

        var mockTrades = new List<FuturesTradeModel>
        {
            new FuturesTradeModel { 
                id = "1", 
                uid = "uid1", 
                type = "limit", 
                side = "buy", 
                price = 50000m, 
                quantity = 1.0m,
                margin = 1000m,
                leverage = 2m,
                liquidation = 45000m,
                stoploss = 0m,
                takeprofit = 55000m,
                pl = 0m,
                creation_ts = 1234567890,
                open = true,
                running = false,
                canceled = false,
                closed = false,
                last_update_ts = 1234567890,
                sum_carry_fees = 0m,
                opening_fee = 10m,
                closing_fee = 0m,
                maintenance_margin = 100m
            },
            new FuturesTradeModel { 
                id = "2", 
                uid = "uid2", 
                type = "limit", 
                side = "buy", 
                price = 51000m, 
                quantity = 0.5m,
                margin = 500m,
                leverage = 2m,
                liquidation = 46000m,
                stoploss = 0m,
                takeprofit = 56000m,
                pl = 0m,
                creation_ts = 1234567891,
                open = true,
                running = false,
                canceled = false,
                closed = false,
                last_update_ts = 1234567891,
                sum_carry_fees = 0m,
                opening_fee = 5m,
                closing_fee = 0m,
                maintenance_margin = 50m
            }
        };

        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(mockTrades), Encoding.UTF8, "application/json")
        };

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.FuturesGetRunningTradesAsync(key, passphrase, secret);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.First().id.Should().Be("1");
        result.First().price.Should().Be(50000m);
        
        _mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => 
                req.Method == HttpMethod.Get &&
                req.RequestUri!.ToString().Contains("/v2/futures") &&
                req.RequestUri!.ToString().Contains("type=running")),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task SwapUsdInBtc_WhenSuccessful_ShouldReturnTrue()
    {
        // Arrange
        var key = "test-key";
        var passphrase = "test-passphrase";
        var secret = "test-secret";
        var amount = 1000;

        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"success\": true}", Encoding.UTF8, "application/json")
        };

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.SwapUsdInBtc(key, passphrase, secret, amount);

        // Assert
        result.Should().BeTrue();
        
        _mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => 
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains("/v2/swap")),
            ItExpr.IsAny<CancellationToken>()
        );
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
            _httpClient?.Dispose();
        }
    }
}