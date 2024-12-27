namespace AutoBot.Models.LnMarkets
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class SubscriptionParams
    {
        [JsonPropertyName("channel")]
        public string Channel { get; set; }

        [JsonPropertyName("data")]
        public JsonElement Data { get; set; }
    }
}
