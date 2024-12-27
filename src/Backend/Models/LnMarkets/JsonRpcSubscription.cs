namespace AutoBot.Models.LnMarkets
{
    using System.Text.Json.Serialization;

    public class JsonRpcSubscription
    {
        [JsonPropertyName("method")]
        public string Method { get; set; }

        [JsonPropertyName("params")]
        public SubscriptionParams Params { get; set; }

        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; }
    }
}
