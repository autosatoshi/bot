using System.Text.Json.Serialization;

namespace AutoBot.Models.LnMarkets;

public class JsonRpcSubscription
    {
        [JsonPropertyName("method")]
        public required string Method { get; set; }

        [JsonPropertyName("params")]
        public required SubscriptionParams Params { get; set; }

        [JsonPropertyName("jsonrpc")]
        public required string JsonRpc { get; set; }
    }
