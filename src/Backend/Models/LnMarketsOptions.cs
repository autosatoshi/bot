using System.ComponentModel.DataAnnotations;

namespace AutoBot.Models;

public class LnMarketsOptions
{
    public const string SectionName = "ln";

    [Required, MinLength(1)]
    public string Key { get; set; } = string.Empty;

    [Required, MinLength(1)]
    public string Passphrase { get; set; } = string.Empty;

    [Required, MinLength(1)]
    public string Secret { get; set; } = string.Empty;

    public bool Pause { get; set; } = true;

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;

    [Range(1, 100)]
    public int Leverage { get; set; } = 1;

    [Range(1, int.MaxValue)]
    public int Takeprofit { get; set; } = 100;

    [Range(1, int.MaxValue)]
    public int MaxTakeprofitPrice { get; set; } = 110000;

    [Range(1, 100)]
    public int MaxRunningTrades { get; set; } = 10;

    [Range(1, int.MaxValue)]
    public int Factor { get; set; } = 1000;

    [Range(0.01, double.MaxValue)]
    public decimal AddMarginInUsd { get; set; } = 1;

    [Range(-100, 0)]
    public int MaxLossInPercent { get; set; } = -50;

    // WebSocket Configuration
    [Range(1, int.MaxValue)]
    public int ReconnectDelaySeconds { get; set; } = 15;

    [Range(1024, int.MaxValue)]
    public int WebSocketBufferSize { get; set; } = 4096;

    [Range(1, int.MaxValue)]
    public int MessageTimeoutSeconds { get; set; } = 5;

    [Range(1, int.MaxValue)]
    public int MinCallIntervalSeconds { get; set; } = 10;

    // Trading Configuration
    [Range(1, int.MaxValue)]
    public int SwapAmount { get; set; } = 2000;

    [Range(1, int.MaxValue)]
    public int ClosedTradesLimit { get; set; } = 1000;

    [Range(1, int.MaxValue)]
    public int DivisorForTimeCalculation { get; set; } = 1000;
}