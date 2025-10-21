using System.Text.Json.Serialization;
using AutoBot.Models.Units;

namespace AutoBot.Models.LnMarkets;

public class LastPriceData
{
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("lastPrice")]
    public Dollar LastPrice { get; set; }

    [JsonPropertyName("lastTickDirection")]
    public required string LastTickDirection { get; set; }
}
