using System.Collections.Concurrent;
using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using Microsoft.Extensions.Options;

namespace AutoBot.Services;

public class PriceQueue : IPriceQueue, IDisposable
{
    private readonly ITradeManager _tradeManager;
    private readonly IOptionsMonitor<LnMarketsOptions> _options;
    private readonly ILogger<PriceQueue> _logger;
    private readonly CancellationTokenSource _exitTokenSource = new();
    private readonly BlockingCollection<LastPriceData> _queue = new();
    private readonly Task _updateLoop;

    public PriceQueue(ITradeManager tradeManager, IOptionsMonitor<LnMarketsOptions> options, ILogger<PriceQueue> logger)
    {
        _tradeManager = tradeManager;
        _options = options;
        _logger = logger;

        _updateLoop = Task.Run(async () =>
        {
            decimal lastPrice = 0;
            var lastIteration = DateTime.MinValue;

            while (!_exitTokenSource.IsCancellationRequested)
            {
                try
                {
                    var data = _queue.Take(_exitTokenSource.Token);

                    // Skip duplicate prices
                    if (data.LastPrice == lastPrice)
                    {
                        continue;
                    }

                    // Skip old messages (timeout check)
                    var timestamp = data.Time?.TimeStampToDateTime() ?? DateTime.MinValue;
                    var timeDelta = DateTime.UtcNow - timestamp.ToUniversalTime();
                    if (timeDelta.TotalSeconds >= _options.CurrentValue.MessageTimeoutSeconds)
                    {
                        continue;
                    }

                    // Skip if too soon since last iteration (rate limiting)
                    if ((DateTime.UtcNow - lastIteration).TotalSeconds < _options.CurrentValue.MinCallIntervalSeconds)
                    {
                        continue;
                    }

                    // Delegate to TradeManager for actual price handling
                    await _tradeManager.HandlePriceUpdateAsync(data);

                    // Update lastPrice only after successful processing
                    lastPrice = data.LastPrice;
                    lastIteration = DateTime.UtcNow;
                }
                catch (OperationCanceledException) when (_exitTokenSource.IsCancellationRequested)
                {
                    _logger.LogDebug("Exiting price queue update loop.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred handling new price data in PriceQueue");
                }
            }
        });
    }

    public void UpdatePrice(LastPriceData data)
    {
        if (!_queue.IsAddingCompleted)
        {
            _queue.Add(data);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                _queue.CompleteAdding();
                _exitTokenSource.Cancel();

                if (!_updateLoop.Wait(TimeSpan.FromSeconds(5)))
                {
                    _logger.LogWarning("PriceQueue update loop did not complete within timeout");
                }

                _queue.Dispose();
                _exitTokenSource.Dispose();
                _updateLoop.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"An error occurred disposing {nameof(PriceQueue)}");
            }
        }

        _logger.LogDebug($"Successfully disposed {nameof(PriceQueue)}");
    }
}
