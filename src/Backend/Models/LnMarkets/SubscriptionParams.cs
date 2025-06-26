using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoBot.Models.LnMarkets;

public class SubscriptionParams
    {
        [JsonPropertyName("channel")]
        public required string Channel { get; set; }

        [JsonPropertyName("data")]
        public JsonElement Data { get; set; }
    }
