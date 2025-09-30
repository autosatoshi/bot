using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using Microsoft.Extensions.Options;

namespace AutoBot.Services;

public class LnMarketsBackgroundService(IPriceQueue _priceQueue, IOptionsMonitor<LnMarketsOptions> _options, ILogger<LnMarketsBackgroundService> _logger) : BackgroundService
{
    private const string FuturesChannel = "futures:btc_usd:last-price";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var client = new ClientWebSocket();

            var uri = new Uri(_options.CurrentValue.Endpoint);
            if (uri.Scheme != "wss")
            {
                _logger.LogWarning("Modifying endpoint scheme from {Scheme} to 'wss'", uri.Scheme);
                var ub = new UriBuilder(uri)
                {
                    Scheme = "wss",
                    Port = uri.IsDefaultPort ? -1 : uri.Port,
                };
                uri = ub.Uri;
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

                var buffer = ArrayPool<byte>.Shared.Rent(_options.CurrentValue.WebSocketBufferSize);
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
                                byte[]? message = null;
                                if (result.EndOfMessage)
                                {
                                    message = new byte[result.Count];
                                    Array.Copy(buffer, 0, message, 0, result.Count);
                                }
                                else
                                {
                                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.CurrentValue.MessageTimeoutSeconds));
                                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);
                                    try
                                    {
                                        message = await AssembleFragmentedMessageAsync(client, buffer, result, linkedCts.Token, _logger);
                                    }
                                    catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                                    {
                                        if (client.State == WebSocketState.Open)
                                        {
                                            _logger.LogWarning("Fragmented message assembly timed out - closing connection to prevent state corruption");
                                            await client.CloseAsync(WebSocketCloseStatus.InternalServerError, "Fragmentation timeout", stoppingToken);
                                        }

                                        break; // Exit message loop to trigger reconnection
                                    }
                                }

                                if (message != null)
                                {
                                    HandleWsTextMessage(message);
                                }

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
                _logger.LogWarning(wsEx, "WebSocket connection failed, retrying in {DelaySeconds}s", _options.CurrentValue.ReconnectDelaySeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Background service stopping due to cancellation");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in background service");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.ReconnectDelaySeconds), stoppingToken);
        }
    }

    private void HandleWsTextMessage(byte[] messageBytes)
    {
        if (messageBytes == null || messageBytes.Length == 0)
        {
            _logger.LogWarning("Received empty or null web socket message");
            return;
        }

        try
        {
            var messageAsString = Encoding.UTF8.GetString(messageBytes);
            var messageType = DetermineMessageType(messageAsString);
            if (messageType != "JsonRpcSubscription")
            {
                return;
            }

            var subscription = JsonSerializer.Deserialize<JsonRpcSubscription>(messageAsString);
            if (subscription == null)
            {
                _logger.LogWarning("Failed to deserialize json rpc subscription: {MessageContent}", messageAsString);
                return;
            }

            if (subscription.Params == null)
            {
                _logger.LogWarning("Subscription missing params: {Message}", messageAsString);
                return;
            }

            if (subscription.Params.Data.ValueKind == JsonValueKind.Undefined || subscription.Params.Data.ValueKind == JsonValueKind.Null)
            {
                _logger.LogWarning("Subscription params missing data for channel {Channel}", subscription.Params.Channel);
                return;
            }

            switch (subscription.Params.Channel)
            {
                case FuturesChannel:
                    var lastPriceData = JsonSerializer.Deserialize<LastPriceData>(subscription.Params.Data.GetRawText());
                    if (lastPriceData == null)
                    {
                        _logger.LogWarning("Failed to deserialize data from json rpc subscription {Subscription}", subscription);
                        return;
                    }

                    _logger.LogInformation("Last Price update: {LastPrice}$", lastPriceData.LastPrice);
                    _priceQueue.UpdatePrice(lastPriceData);
                    return;
                default:
                    _logger.LogWarning("Received subscription data for unknown channel: {Channel}", subscription.Params.Channel);
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing web socket text message");
            return;
        }
    }

    private static async Task<byte[]?> AssembleFragmentedMessageAsync(ClientWebSocket client, byte[] buffer, WebSocketReceiveResult firstResult, CancellationToken cancellationToken, ILogger? logger = null)
    {
        logger?.LogDebug("Receiving fragmented WebSocket message");

        var fragments = new List<byte[]> { buffer[..firstResult.Count] };
        var totalLength = firstResult.Count;

        WebSocketReceiveResult result;
        do
        {
            result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType != WebSocketMessageType.Text)
            {
                logger?.LogWarning("Unexpected message type during fragmented message assembly: {MessageType}", result.MessageType);
                return null;
            }

            fragments.Add(buffer[..result.Count]);
            totalLength += result.Count;
        }
        while (!result.EndOfMessage);

        // Combine all fragments
        var assembledMessage = new byte[totalLength];
        var offset = 0;
        foreach (var fragment in fragments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fragment.CopyTo(assembledMessage, offset);
            offset += fragment.Length;
        }

        logger?.LogDebug("Assembled fragmented message: {TotalLength} bytes from {FragmentCount} fragments", totalLength, fragments.Count);
        return assembledMessage;
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
