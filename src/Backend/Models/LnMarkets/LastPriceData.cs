namespace AutoBot.Models.LnMarkets
{
    using System.Text.Json.Serialization;

    public class LastPriceData
    {
        [JsonPropertyName("time")]
        public long Time { get; set; }

        [JsonPropertyName("lastPrice")]
        public decimal LastPrice { get; set; }

        [JsonPropertyName("lastTickDirection")]
        public string LastTickDirection { get; set; }
    }
}
