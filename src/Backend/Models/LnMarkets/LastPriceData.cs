using System.Text.Json.Serialization;

namespace AutoBot.Models.LnMarkets;

public class LastPriceData
    {
        [JsonPropertyName("time")]
        public string? Time { get; set; }

        [JsonPropertyName("lastPrice")]
        public decimal LastPrice { get; set; }

        [JsonPropertyName("lastTickDirection")]
        public required string LastTickDirection { get; set; }
    }
