using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using Microsoft.Extensions.Options;

namespace AutoBot.Services;

public class LnMarketsClient : IMarketplaceClient
{
    private readonly HttpClient _client;
    private readonly ILogger<LnMarketsClient> _logger;

    public LnMarketsClient(IHttpClientFactory factory, IOptions<LnMarketsOptions> options, ILogger<LnMarketsClient> logger)
    {
        _client = factory.CreateClient();
        _client.BaseAddress = new Uri(options.Value.Endpoint);
        _logger = logger;
    }

    public async Task<UserModel?> GetUser(string key, string passphrase, string secret)
    {
        const string path = "/v2/user";
        const string queryParams = "";
        return await ExecuteGetRequestAsync<UserModel>(_client, key, passphrase, secret, path, queryParams, nameof(GetUser), _logger);
    }

    public async Task<IReadOnlyList<FuturesTradeModel>> GetRunningTrades(string key, string passphrase, string secret)
    {
        const string path = "/v2/futures";
        const string queryParams = "type=running";
        return await ExecuteGetRequestAsync<List<FuturesTradeModel>>(_client, key, passphrase, secret, path, queryParams, nameof(GetRunningTrades), _logger) ?? [];
    }

    public async Task<bool> AddMargin(string key, string passphrase, string secret, string id, long amountInSats)
    {
        const string path = "/v2/futures/add-margin";
        var requestBody = JsonSerializer.Serialize(new { id = id, amount = amountInSats });
        return await ExecutePostRequestAsync(_client, key, passphrase, secret, path, requestBody, nameof(AddMargin), _logger);
    }

    public async Task<bool> SwapUsdToBtc(string key, string passphrase, string secret, int amountInUsd)
    {
        const string path = "/v2/swap";
        var requestBody = JsonSerializer.Serialize(new { in_asset = "USD", out_asset = "BTC", in_amount = amountInUsd });
        return await ExecutePostRequestAsync(_client, key, passphrase, secret, path, requestBody, nameof(SwapUsdToBtc), _logger);
    }

    public async Task<bool> CreateNewTrade(string key, string passphrase, string secret, decimal exitPriceInUsd, int leverage, double quantityInUsd)
    {
        const string path = "/v2/futures";
        var requestBody = JsonSerializer.Serialize(new { side = "b", type = "m", takeprofit = exitPriceInUsd, leverage = leverage, quantity = quantityInUsd });
        return await ExecutePostRequestAsync(_client, key, passphrase, secret, path, requestBody, nameof(CreateNewTrade), _logger);
    }

    private static async Task<bool> ExecutePostRequestAsync(HttpClient client, string key, string passphrase, string secret, string path, string requestBody, string operationName, ILogger? logger = null)
    {
        try
        {
            using var request = CreateAuthenticatedPostRequest(path, key, passphrase, secret, requestBody);
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Exception occurred while executing {Operation}", operationName);
            return false;
        }
    }

    private static async Task<T?> ExecuteGetRequestAsync<T>(HttpClient client, string key, string passphrase, string secret, string path, string queryParams, string operationName, ILogger? logger = null)
        where T : class
    {
        try
        {
            using var request = CreateAuthenticatedGetRequest(path, key, passphrase, secret, queryParams);
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Exception occurred while executing {Operation}", operationName);
            return null;
        }
    }

    private static HttpRequestMessage CreateAuthenticatedPostRequest(string path, string key, string passphrase, string secret, string requestBody)
    {
        var timestamp = (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        var signaturePayload = $"{timestamp}POST{path}{requestBody}";
        var signature = CreateSignature(secret, signaturePayload);

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };
        AddAuthHeaders(request, key, passphrase, signature, timestamp);
        return request;
    }

    private static HttpRequestMessage CreateAuthenticatedGetRequest(string path, string key, string passphrase, string secret, string queryParams)
    {
        var timestamp = (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        var signaturePayload = $"{timestamp}GET{path}{queryParams}";
        var signature = CreateSignature(secret, signaturePayload);

        var url = string.IsNullOrEmpty(queryParams) ? path : $"{path}?{queryParams}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuthHeaders(request, key, passphrase, signature, timestamp);
        return request;
    }

    private static string CreateSignature(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }

    private static void AddAuthHeaders(HttpRequestMessage request, string key, string passphrase, string signature, long timestamp)
    {
        request.Headers.Add("LNM-ACCESS-KEY", key);
        request.Headers.Add("LNM-ACCESS-PASSPHRASE", passphrase);
        request.Headers.Add("LNM-ACCESS-SIGNATURE", signature);
        request.Headers.Add("LNM-ACCESS-TIMESTAMP", timestamp.ToString());
    }
}
