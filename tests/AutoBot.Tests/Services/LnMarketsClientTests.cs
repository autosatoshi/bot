using AutoBot.Models;
using AutoBot.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;

namespace AutoBot.Tests.Services;

public sealed class LnMarketsClientTests : IDisposable
{
    private readonly Mock<ILogger<LnMarketsClient>> _mockLogger;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<IOptions<LnMarketsOptions>> _mockOptions;
    private readonly LnMarketsClient _client;

    public LnMarketsClientTests()
    {
        _mockLogger = new Mock<ILogger<LnMarketsClient>>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        
        // Create a fake IHttpClientFactory that returns our test HttpClient
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
    public async Task AddMargin_WhenSuccessful_ShouldReturnTrue()
    {
        // Arrange
        var key = "test-key";
        var passphrase = "test-passphrase";
        var secret = "test-secret";
        var id = "test-id";
        var amount = 100L;

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
        var result = await _client.AddMarginInSats(key, passphrase, secret, id, amount);

        // Assert
        result.Should().BeTrue();
        
        _mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => 
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains("/v2/futures/add-margin")),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task AddMargin_WhenHttpRequestFails_ShouldReturnFalse()
    {
        // Arrange
        var key = "test-key";
        var passphrase = "test-passphrase";
        var secret = "test-secret";
        var id = "test-id";
        var amount = 100L;

        var mockResponse = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\": \"Bad request\"}", Encoding.UTF8, "application/json")
        };

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _client.AddMarginInSats(key, passphrase, secret, id, amount);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AddMargin_WhenExceptionOccurs_ShouldReturnFalse()
    {
        // Arrange
        var key = "test-key";
        var passphrase = "test-passphrase";
        var secret = "test-secret";
        var id = "test-id";
        var amount = 100L;

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _client.AddMarginInSats(key, passphrase, secret, id, amount);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AddMargin_ShouldSetCorrectHeaders()
    {
        // Arrange
        var key = "test-key";
        var passphrase = "test-passphrase";
        var secret = "test-secret";
        var id = "test-id";
        var amount = 100L;

        HttpRequestMessage? capturedRequest = null;
        string? capturedRequestBody = null;

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
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        await _client.AddMarginInSats(key, passphrase, secret, id, amount);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Should().Contain(h => h.Key == "LNM-ACCESS-KEY" && h.Value.Contains(key));
        capturedRequest!.Headers.Should().Contain(h => h.Key == "LNM-ACCESS-PASSPHRASE" && h.Value.Contains(passphrase));
        capturedRequest!.Headers.Should().Contain(h => h.Key == "LNM-ACCESS-SIGNATURE");
        capturedRequest!.Headers.Should().Contain(h => h.Key == "LNM-ACCESS-TIMESTAMP");
        
        // Verify request body contains correct data
        capturedRequestBody.Should().NotBeNull();
        capturedRequestBody.Should().Contain($"\"id\":\"{id}\"");
        capturedRequestBody.Should().Contain($"\"amount\":{amount}");
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