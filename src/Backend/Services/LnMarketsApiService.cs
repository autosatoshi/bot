using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AutoBot.Models.LnMarkets;

namespace AutoBot.Services;

public class LnMarketsApiService : ILnMarketsApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<LnMarketsApiService> _logger;
        private readonly string _lnMarketsEndpoint = "https://api.lnmarkets.com";

        public LnMarketsApiService(IHttpClientFactory httpClientFactory, ILogger<LnMarketsApiService> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
        }

        public async Task<bool> AddMargin(string key, string passphrase, string secret, string id, int amount)
        {
            var method = "POST";
            var path = "/v2/futures/add-margin";
            var @params = $$"""{"id":"{{id}}","amount":{{amount}}}""";
            var timestamp = GetUtcNowInUnixTimestamp();

            SetLnMarketsHeaders(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);
            var content = new StringContent(@params, Encoding.UTF8, "application/json");
            
            try
            {
                var response = await _httpClient.PostAsync($"{_lnMarketsEndpoint}{path}", content);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("AddMargin successful for id: {Id}, amount: {Amount}", id, amount);
                    return true;
                }
                
                _logger.LogWarning("AddMargin failed for id: {Id}, amount: {Amount}. Status: {StatusCode}, Response: {Response}", 
                    id, amount, response.StatusCode, responseContent);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while adding margin for id: {Id}, amount: {Amount}", id, amount);
                return false;
            }
            finally
            {
                ClearLnMarketsHeaders();
            }
        }

        public async Task<bool> Cancel(string key, string passphrase, string secret, string id)
        {
            var method = "POST";
            var path = "/v2/futures/cancel";
            var @params = $"{{\"id\":\"{id}\"}}";
            var timestamp = GetUtcNowInUnixTimestamp();

            SetLnMarketsHeaders(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);
            var content = new StringContent(@params, Encoding.UTF8, "application/json");
            
            try
            {
                var response = await _httpClient.PostAsync($"{_lnMarketsEndpoint}{path}", content);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Cancel successful for id: {Id}", id);
                    return true;
                }
                
                _logger.LogWarning("Cancel failed for id: {Id}. Status: {StatusCode}, Response: {Response}", 
                    id, response.StatusCode, responseContent);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while canceling trade for id: {Id}", id);
                return false;
            }
            finally
            {
                ClearLnMarketsHeaders();
            }
        }

        public async Task<bool> CreateLimitBuyOrder(string key, string passphrase, string secret, decimal price, decimal takeprofit, int leverage, double quantity)
        {
            var method = "POST";
            var path = "/v2/futures";
            var @params = $$"""{"side":"b","type":"l","price":{{price}},"takeprofit":{{takeprofit}},"leverage":{{leverage}},"quantity":{{quantity.ToString(CultureInfo.InvariantCulture)}}}""";
            var timestamp = GetUtcNowInUnixTimestamp();

            SetLnMarketsHeaders(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);
            var content = new StringContent(@params, Encoding.UTF8, "application/json");
            
            try
            {
                var response = await _httpClient.PostAsync($"{_lnMarketsEndpoint}{path}", content);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("CreateLimitBuyOrder successful for price: {Price}, takeprofit: {TakeProfit}, leverage: {Leverage}, quantity: {Quantity}", 
                        price, takeprofit, leverage, quantity);
                    return true;
                }
                
                _logger.LogWarning("CreateLimitBuyOrder failed for price: {Price}, takeprofit: {TakeProfit}, leverage: {Leverage}, quantity: {Quantity}. Status: {StatusCode}, Response: {Response}", 
                    price, takeprofit, leverage, quantity, response.StatusCode, responseContent);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while creating limit buy order for price: {Price}, takeprofit: {TakeProfit}, leverage: {Leverage}, quantity: {Quantity}", 
                    price, takeprofit, leverage, quantity);
                return false;
            }
            finally
            {
                ClearLnMarketsHeaders();
            }
        }

        public async Task<bool> CreateNewSwap(string key, string passphrase, string secret)
        {
            var method = "POST";
            var path = "/v2/swap";
            var @params = """{"in_asset":"BTC","out_asset":"USD","in_amount":2000}""";
            var timestamp = GetUtcNowInUnixTimestamp();
            var sigPayload = $"{timestamp}{method}{path}{@params}";

            SetLnMarketsHeaders(key, passphrase, GetSignature(secret, sigPayload), timestamp);
            var content = new StringContent(@params, Encoding.UTF8, "application/json");
            
            try
            {
                var response = await _httpClient.PostAsync($"{_lnMarketsEndpoint}{path}", content);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("CreateNewSwap successful for BTC to USD swap with amount: 2000");
                    return true;
                }
                
                _logger.LogWarning("CreateNewSwap failed for BTC to USD swap with amount: 2000. Status: {StatusCode}, Response: {Response}", 
                    response.StatusCode, responseContent);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while creating new swap for BTC to USD with amount: 2000");
                return false;
            }
            finally
            {
                ClearLnMarketsHeaders();
            }
        }

        public async Task<bool> SwapUsdInBtc(string key, string passphrase, string secret, int amount)
        {
            var method = "POST";
            var path = "/v2/swap";
            var @params = $$"""{"in_asset":"USD","out_asset":"BTC","in_amount":{{amount}}}""";
            var timestamp = GetUtcNowInUnixTimestamp();
            var sigPayload = $"{timestamp}{method}{path}{@params}";

            SetLnMarketsHeaders(key, passphrase, GetSignature(secret, sigPayload), timestamp);
            var content = new StringContent(@params, Encoding.UTF8, "application/json");
            
            try
            {
                var response = await _httpClient.PostAsync($"{_lnMarketsEndpoint}{path}", content);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("SwapUsdInBtc successful for USD to BTC swap with amount: {Amount}", amount);
                    return true;
                }
                
                _logger.LogWarning("SwapUsdInBtc failed for USD to BTC swap with amount: {Amount}. Status: {StatusCode}, Response: {Response}", 
                    amount, response.StatusCode, responseContent);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while swapping USD to BTC with amount: {Amount}", amount);
                return false;
            }
            finally
            {
                ClearLnMarketsHeaders();
            }
        }

        public async Task<IEnumerable<FuturesTradeModel>> FuturesGetClosedTradesAsync(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/futures";
            var @params = "type=closed&limit=1000";
            var timestamp = GetUtcNowInUnixTimestamp();

            SetLnMarketsHeaders(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);
            
            try
            {
                var data = await _httpClient.GetFromJsonAsync<IEnumerable<FuturesTradeModel>>($"{_lnMarketsEndpoint}{path}?{@params}") ?? new List<FuturesTradeModel>();
                _logger.LogDebug("FuturesGetClosedTradesAsync successful, retrieved {Count} closed trades", data.Count());
                return data.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while retrieving closed futures trades");
                return new List<FuturesTradeModel>();
            }
            finally
            {
                ClearLnMarketsHeaders();
            }
        }

        public async Task<IEnumerable<FuturesTradeModel>> FuturesGetOpenTradesAsync(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/futures";
            var @params = $"type=open";
            var timestamp = GetUtcNowInUnixTimestamp();

            SetLnMarketsHeaders(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);
            
            try
            {
                var data = await _httpClient.GetFromJsonAsync<IEnumerable<FuturesTradeModel>>($"{_lnMarketsEndpoint}{path}?{@params}") ?? new List<FuturesTradeModel>();
                _logger.LogDebug("FuturesGetOpenTradesAsync successful, retrieved {Count} open trades", data.Count());
                return data.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while retrieving open futures trades");
                return new List<FuturesTradeModel>();
            }
            finally
            {
                ClearLnMarketsHeaders();
            }
        }

        public async Task<IEnumerable<FuturesTradeModel>> FuturesGetRunningTradesAsync(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/futures";
            var @params = $"type=running";
            var timestamp = GetUtcNowInUnixTimestamp();

            SetLnMarketsHeaders(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);
            
            try
            {
                var data = await _httpClient.GetFromJsonAsync<IEnumerable<FuturesTradeModel>>($"{_lnMarketsEndpoint}{path}?{@params}") ?? new List<FuturesTradeModel>();
                _logger.LogDebug("FuturesGetRunningTradesAsync successful, retrieved {Count} running trades", data.Count());
                return data.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while retrieving running futures trades");
                return new List<FuturesTradeModel>();
            }
            finally
            {
                ClearLnMarketsHeaders();
            }
        }

        public async Task<IEnumerable<DepositModel>> GetDeposits(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/user/deposit";
            var @params = "";
            var timestamp = GetUtcNowInUnixTimestamp();

            SetLnMarketsHeaders(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);
            
            try
            {
                var data = await _httpClient.GetFromJsonAsync<IEnumerable<DepositModel>>($"{_lnMarketsEndpoint}{path}?{@params}") ?? new List<DepositModel>();
                _logger.LogDebug("GetDeposits successful, retrieved {Count} deposits", data.Count());
                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while retrieving deposits");
                return new List<DepositModel>();
            }
            finally
            {
                ClearLnMarketsHeaders();
            }
        }

        public async Task<UserModel> GetUser(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/user";
            var timestamp = GetUtcNowInUnixTimestamp();

            SetLnMarketsHeaders(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}"), timestamp);
            
            try
            {
                var data = await _httpClient.GetFromJsonAsync<UserModel>($"{_lnMarketsEndpoint}{path}?");
                if (data == null)
                {
                    _logger.LogWarning("GetUser returned null data from LN Markets API");
                    throw new InvalidOperationException("Failed to retrieve user data from LN Markets API");
                }
                
                _logger.LogDebug("GetUser successful, retrieved user data");
                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while retrieving user data");
                throw;
            }
            finally
            {
                ClearLnMarketsHeaders();
            }
        }

        public async Task<string> GetWithdrawals(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/user/withdraw";
            var @params = "";
            var timestamp = GetUtcNowInUnixTimestamp();

            SetLnMarketsHeaders(key, passphrase, GetSignature(secret, $"{timestamp}{method}{path}{@params}"), timestamp);
            
            try
            {
                var data = await _httpClient.GetStringAsync($"{_lnMarketsEndpoint}{path}?{@params}");
                _logger.LogDebug("GetWithdrawals successful, retrieved withdrawals data");
                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while retrieving withdrawals");
                throw;
            }
            finally
            {
                ClearLnMarketsHeaders();
            }
        }

        private void SetLnMarketsHeaders(string key, string passphrase, string signature, long timestamp)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("LNM-ACCESS-KEY", key);
            _httpClient.DefaultRequestHeaders.Add("LNM-ACCESS-PASSPHRASE", passphrase);
            _httpClient.DefaultRequestHeaders.Add("LNM-ACCESS-SIGNATURE", signature);
            _httpClient.DefaultRequestHeaders.Add("LNM-ACCESS-TIMESTAMP", timestamp.ToString());
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json");
        }

        private void ClearLnMarketsHeaders()
        {
            _httpClient.DefaultRequestHeaders.Clear();
        }

        private string GetSignature(string secret, string payload)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToBase64String(hash);
        }

        private static long GetUtcNowInUnixTimestamp() => (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
    }
