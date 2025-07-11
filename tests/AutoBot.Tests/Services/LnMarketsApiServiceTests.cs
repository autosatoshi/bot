using AutoBot.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;

namespace AutoBot.Tests.Services;

public class LnMarketsApiServiceTests : IDisposable
{
    private readonly Mock<ILogger<LnMarketsApiService>> _mockLogger;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly LnMarketsApiService _service;

    public LnMarketsApiServiceTests()
    {
        _mockLogger = new Mock<ILogger<LnMarketsApiService>>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        
        // Create a fake IHttpClientFactory that returns our test HttpClient
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
        
        _service = new LnMarketsApiService(mockFactory.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task AddMargin_WhenSuccessful_ShouldReturnTrue()
    {
        // Arrange
        var key = "test-key";
        var passphrase = "test-passphrase";
        var secret = "test-secret";
        var id = "test-id";
        var amount = 100;

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
        var result = await _service.AddMargin(key, passphrase, secret, id, amount);

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
        var amount = 100;

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
        var result = await _service.AddMargin(key, passphrase, secret, id, amount);

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
        var amount = 100;

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _service.AddMargin(key, passphrase, secret, id, amount);

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
        var amount = 100;

        HttpRequestMessage? capturedRequest = null;

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        await _service.AddMargin(key, passphrase, secret, id, amount);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Should().Contain(h => h.Key == "LNM-ACCESS-KEY" && h.Value.Contains(key));
        capturedRequest!.Headers.Should().Contain(h => h.Key == "LNM-ACCESS-PASSPHRASE" && h.Value.Contains(passphrase));
        capturedRequest!.Headers.Should().Contain(h => h.Key == "LNM-ACCESS-SIGNATURE");
        capturedRequest!.Headers.Should().Contain(h => h.Key == "LNM-ACCESS-TIMESTAMP");
        
        // Verify request body contains correct data
        var requestBody = await capturedRequest.Content!.ReadAsStringAsync();
        requestBody.Should().Contain($"\"id\":\"{id}\"");
        requestBody.Should().Contain($"\"amount\":{amount}");
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}