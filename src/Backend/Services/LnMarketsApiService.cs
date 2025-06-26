using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AutoBot.Models.LnMarkets;

namespace AutoBot.Services;

public class LnMarketsApiService : ILnMarketsApiService
    {
        private readonly string _lnMarketsEndpoint = "https://api.lnmarkets.com";

        public async Task<bool> AddMargin(string key, string passphrase, string secret, string id, int amount)
        {
            var method = "POST";
            var path = "/v2/futures/add-margin";
            var @params = $"{{" +
                $"\"id\":\"{id}\"," +
                $"\"amount\":{amount}" +
                $"}}";
            var timestamp = GetUtcNowInUnixTimestamp();

            using var client = GetLnMarketsHttpClient(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);
            var content = new StringContent(@params, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{_lnMarketsEndpoint}{path}", content);
            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine(responseContent);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> Cancel(string key, string passphrase, string secret, string id)
        {
            var method = "POST";
            var path = "/v2/futures/cancel";
            var @params = $"{{\"id\":\"{id}\"}}";
            var timestamp = GetUtcNowInUnixTimestamp();

            using var client = GetLnMarketsHttpClient(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);

            var content = new StringContent(@params, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{_lnMarketsEndpoint}{path}", content);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> CreateLimitBuyOrder(string key, string passphrase, string secret, decimal price, decimal takeprofit, int leverage, double quantity)
        {
            var method = "POST";
            var path = "/v2/futures";
            var @params = $"{{" +
                $"\"side\":\"b\"," +
                $"\"type\":\"l\"," +
                $"\"price\":{price}," +
                $"\"takeprofit\":{takeprofit}," +
                $"\"leverage\":{leverage}," +
                $"\"quantity\":{quantity.ToString(CultureInfo.InvariantCulture)}" +
                $"}}";
            var timestamp = GetUtcNowInUnixTimestamp();

            using var client = GetLnMarketsHttpClient(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);
            var content = new StringContent(@params, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{_lnMarketsEndpoint}{path}", content);
            var responseContent = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> CreateNewSwap(string key, string passphrase, string secret)
        {
            var method = "POST";
            var path = "/v2/swap";
            var @params = "{" + @"""in_asset"":""BTC"",""out_asset"":""USD"",""in_amount"":2000}";
            Console.WriteLine(@params);
            var timestamp = GetUtcNowInUnixTimestamp();
            var sigPayload = $"{timestamp}{method}{path}{@params}";
            Console.WriteLine(sigPayload);

            using var client = GetLnMarketsHttpClient(key, passphrase, GetSignature(secret, sigPayload), timestamp);
            var content = new StringContent(@params, Encoding.UTF8, "application/json");
            Console.WriteLine(await content.ReadAsStringAsync());
            var response = await client.PostAsync($"{_lnMarketsEndpoint}{path}", content);
            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine(responseContent);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> SwapUsdInBtc(string key, string passphrase, string secret, int amount)
        {
            var method = "POST";
            var path = "/v2/swap";
            var @params = "{" + @"""in_asset"":""USD"",""out_asset"":""BTC"",""in_amount"":" + amount + "}";
            Console.WriteLine(@params);
            var timestamp = GetUtcNowInUnixTimestamp();
            var sigPayload = $"{timestamp}{method}{path}{@params}";
            Console.WriteLine(sigPayload);

            using var client = GetLnMarketsHttpClient(key, passphrase, GetSignature(secret, sigPayload), timestamp);
            var content = new StringContent(@params, Encoding.UTF8, "application/json");
            Console.WriteLine(await content.ReadAsStringAsync());
            var response = await client.PostAsync($"{_lnMarketsEndpoint}{path}", content);
            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine(responseContent);
            return response.IsSuccessStatusCode;
        }

        public async Task<IEnumerable<FuturesTradeModel>> FuturesGetClosedTradesAsync(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/futures";
            var @params = $"type=closed&limit=1000";
            var timestamp = GetUtcNowInUnixTimestamp();

            using var client = GetLnMarketsHttpClient(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);
            var data = await client.GetFromJsonAsync<IEnumerable<FuturesTradeModel>>($"{_lnMarketsEndpoint}{path}?{@params}") ?? new List<FuturesTradeModel>();
            return data.ToList();
        }

        public async Task<IEnumerable<FuturesTradeModel>> FuturesGetOpenTradesAsync(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/futures";
            var @params = $"type=open";
            var timestamp = GetUtcNowInUnixTimestamp();

            using var client = GetLnMarketsHttpClient(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);
            var data = await client.GetFromJsonAsync<IEnumerable<FuturesTradeModel>>($"{_lnMarketsEndpoint}{path}?{@params}") ?? new List<FuturesTradeModel>();
            return data.ToList();
        }

        public async Task<IEnumerable<FuturesTradeModel>> FuturesGetRunningTradesAsync(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/futures";
            var @params = $"type=running";
            var timestamp = GetUtcNowInUnixTimestamp();

            using var client = GetLnMarketsHttpClient(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);
            var data = await client.GetFromJsonAsync<IEnumerable<FuturesTradeModel>>($"{_lnMarketsEndpoint}{path}?{@params}") ?? new List<FuturesTradeModel>();
            return data.ToList();
        }

        public async Task<IEnumerable<DepositModel>> GetDeposits(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/user/deposit";
            var @params = "";
            var timestamp = GetUtcNowInUnixTimestamp();

            using var client = GetLnMarketsHttpClient(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);
            var data = await client.GetFromJsonAsync<IEnumerable<DepositModel>>($"{_lnMarketsEndpoint}{path}?{@params}") ?? new List<DepositModel>();
            return data;
        }

        public async Task<UserModel> GetUser(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/user";
            var timestamp = GetUtcNowInUnixTimestamp();

            using var client = GetLnMarketsHttpClient(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}"), timestamp);
            var data2 = await client.GetFromJsonAsync<UserModel>($"{_lnMarketsEndpoint}{path}?") ?? throw new Exception();
            return data2;
        }

        public async Task<string> GetWithdrawals(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/user/withdraw";
            var @params = "";
            var timestamp = GetUtcNowInUnixTimestamp();

            using var client = GetLnMarketsHttpClient(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);
            var data = await client.GetStringAsync($"{_lnMarketsEndpoint}{path}?{@params}");
            return data;
        }

        private HttpClient GetLnMarketsHttpClient(string key, string passphrase, string signature, long timestamp)
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("LNM-ACCESS-KEY", key);
            httpClient.DefaultRequestHeaders.Add("LNM-ACCESS-PASSPHRASE", passphrase);
            httpClient.DefaultRequestHeaders.Add("LNM-ACCESS-SIGNATURE", signature);
            httpClient.DefaultRequestHeaders.Add("LNM-ACCESS-TIMESTAMP", timestamp.ToString());
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json");

            return httpClient;
        }

        private string GetSignature(string secret, string payload)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToBase64String(hash);
        }

        private static long GetUtcNowInUnixTimestamp() => (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
    }
