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

public sealed class LnMarketsClientRefactoredTests : IDisposable
{
    private readonly Mock<ILogger<LnMarketsClient>> _mockLogger;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<IOptions<LnMarketsOptions>> _mockOptions;
    private readonly LnMarketsClient _client;

    public LnMarketsClientRefactoredTests()
    {
        _mockLogger = new Mock<ILogger<LnMarketsClient>>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
        
        // Create mock options with default values
        _mockOptions = new Mock<IOptions<LnMarketsOptions>>();
        _mockOptions.Setup(o => o.Value).Returns(new LnMarketsOptions 
        { 
            Endpoint = "https://test.endpoint" 
        });
        
        _client = new LnMarketsClient(mockFactory.Object, _mockOptions.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task CreateNewTrade_WhenSuccessful_ShouldReturnTrue()
    {
        // Arrange
        var key = "test-key";
        var passphrase = "test-passphrase";
        var secret = "test-secret";
        var takeprofit = 51000m;
        var leverage = 2;
        var quantity = 1.5;

        HttpRequestMessage? capturedRequest = null;
        string? capturedRequestBody = null;

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
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) => 
            {
                capturedRequest = req;
                if (req.Content != null)
                {
                    capturedRequestBody = await req.Content.ReadAsStringAsync();
                }
            })
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _client.CreateNewTrade(key, passphrase, secret, takeprofit, leverage, quantity);

        // Assert
        result.Should().BeTrue();
        
        // Verify endpoint and HTTP method
        _mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => 
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains("/v2/futures")),
            ItExpr.IsAny<CancellationToken>()
        );

        // Verify request body content
        capturedRequestBody.Should().NotBeNull();
        capturedRequestBody.Should().Contain("\"type\":\"m\"");
        capturedRequestBody.Should().Contain("\"side\":\"b\"");
        capturedRequestBody.Should().Contain($"\"takeprofit\":{takeprofit.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        capturedRequestBody.Should().Contain($"\"leverage\":{leverage}");
        capturedRequestBody.Should().Contain($"\"quantity\":{quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    }

    [Fact]
    public async Task GetRunningTrades_WhenSuccessful_ShouldReturnTrades()
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
                margin = 1000L,
                leverage = 2m,
                liquidation = 45000m,
                stoploss = 0m,
                takeprofit = 55000m,
                pl = 0L,
                creation_ts = 1234567890,
                open = true,
                running = false,
                canceled = false,
                closed = false,
                last_update_ts = 1234567890,
                sum_carry_fees = 0L,
                opening_fee = 10L,
                closing_fee = 0L,
                maintenance_margin = 100L
            },
            new FuturesTradeModel { 
                id = "2", 
                uid = "uid2", 
                type = "limit", 
                side = "buy", 
                price = 51000m, 
                quantity = 0.5m,
                margin = 500L,
                leverage = 2m,
                liquidation = 46000m,
                stoploss = 0m,
                takeprofit = 56000m,
                pl = 0L,
                creation_ts = 1234567891,
                open = true,
                running = false,
                canceled = false,
                closed = false,
                last_update_ts = 1234567891,
                sum_carry_fees = 0L,
                opening_fee = 5L,
                closing_fee = 0L,
                maintenance_margin = 50L
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
        var result = await _client.GetRunningTrades(key, passphrase, secret);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.First().id.Should().Be("1");
        result.First().price.Value.Should().Be(50000m);
        
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
        var result = await _client.SwapUsdToBtc(key, passphrase, secret, amount);

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