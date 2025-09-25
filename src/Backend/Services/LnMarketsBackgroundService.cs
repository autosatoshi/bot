using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using Microsoft.Extensions.Options;

namespace AutoBot.Services;

public class LnMarketsBackgroundService(IPriceQueue _priceQueue, IOptions<LnMarketsOptions> _options, ILogger<LnMarketsBackgroundService> _logger) : BackgroundService
{
    private const string FuturesChannel = "futures:btc_usd:last-price";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var client = new ClientWebSocket();

            var uri = new Uri(_options.Value.Endpoint);
            if (uri.Scheme != "wss")
            {
                _logger.LogWarning("Modifying endpoint scheme from '{}' to 'wss'", uri.Scheme);
                uri = new Uri("wss://" + uri.Host);
            }

            try
            {
                await client.ConnectAsync(uri, stoppingToken);

                const string JsonRpcVersion = "2.0";
                const string SubscribeMethod = "v1/public/subscribe";

                var payload = $"{{\"jsonrpc\":\"{JsonRpcVersion}\",\"id\":\"{Guid.NewGuid()}\",\"method\":\"{SubscribeMethod}\",\"params\":[\"{FuturesChannel}\"]}}";
                var messageBuffer = Encoding.UTF8.GetBytes(payload);
                var segment = new ArraySegment<byte>(messageBuffer);

                await client.SendAsync(segment, WebSocketMessageType.Text, true, stoppingToken);

                var buffer = ArrayPool<byte>.Shared.Rent(_options.Value.WebSocketBufferSize);
                try
                {
                    while (client.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                    {
                        var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);
                        switch (result.MessageType)
                        {
                            case WebSocketMessageType.Close:
                                await client.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, stoppingToken);
                                break;
                            case WebSocketMessageType.Text:
                                HandleWsTextMessage(buffer, result);
                                break;
                            default:
                                break;
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (WebSocketException wsEx)
            {
                _logger.LogWarning(wsEx, "WebSocket connection failed, retrying in {DelaySeconds}s", _options.Value.ReconnectDelaySeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Background service stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in background service");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.Value.ReconnectDelaySeconds), stoppingToken);
        }
    }

    private void HandleWsTextMessage(byte[] buffer, WebSocketReceiveResult result)
    {
        if (buffer == null || result.Count <= 0)
        {
            _logger.LogWarning("Received empty or null web socket message");
            return;
        }

        try
        {
            var messageAsString = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var messageType = DetermineMessageType(messageAsString);
            if (messageType != "JsonRpcSubscription")
            {
                return;
            }

            var subscription = JsonSerializer.Deserialize<JsonRpcSubscription>(messageAsString);
            if (subscription == null)
            {
                _logger.LogWarning("Failed to deserialize json rpc subscription: {}", messageAsString);
                return;
            }

            switch (subscription.Params.Channel)
            {
                case FuturesChannel:
                    var lastPriceData = JsonSerializer.Deserialize<LastPriceData>(subscription.Params.Data.GetRawText());
                    if (lastPriceData == null)
                    {
                        _logger.LogWarning("Failed to deserialize data from json rpc subscription {}", subscription);
                        return;
                    }

                    _logger.LogInformation("Last Price update: {}$", lastPriceData.LastPrice);
                    _priceQueue.UpdatePrice(lastPriceData);
                    return;
                default:
                    _logger.LogWarning("Received subscription data for unknown channel: {}", subscription.Params.Channel);
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing web socket text message");
            return;
        }
    }

    private static string DetermineMessageType(string jsonMessage)
    {
        using var doc = JsonDocument.Parse(jsonMessage);
        if (doc.RootElement.TryGetProperty("result", out _))
        {
            return "JsonRpcResponse";
        }
        else if (doc.RootElement.TryGetProperty("method", out _) && doc.RootElement.TryGetProperty("params", out _))
        {
            return "JsonRpcSubscription";
        }

        return "Unknown";
    }
}
