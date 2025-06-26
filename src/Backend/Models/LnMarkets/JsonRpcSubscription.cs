using System.Text.Json.Serialization;

namespace AutoBot.Models.LnMarkets;

public class JsonRpcSubscription
    {
        [JsonPropertyName("method")]
        public string Method { get; set; }

        [JsonPropertyName("params")]
        public SubscriptionParams Params { get; set; }

        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; }
    }
