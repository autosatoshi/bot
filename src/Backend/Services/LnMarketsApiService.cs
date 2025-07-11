﻿using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AutoBot.Models;
using AutoBot.Models.LnMarkets;
using Microsoft.Extensions.Options;

namespace AutoBot.Services;

public class LnMarketsApiService : ILnMarketsApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<LnMarketsApiService> _logger;
        private readonly IOptions<LnMarketsOptions> _options;
        private readonly string _lnMarketsEndpoint = "https://api.lnmarkets.com";

        public LnMarketsApiService(IHttpClientFactory httpClientFactory, ILogger<LnMarketsApiService> logger, IOptions<LnMarketsOptions> options)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
            _options = options;
        }

        public async Task<bool> AddMargin(string key, string passphrase, string secret, string id, int amount)
        {
            var method = "POST";
            var path = "/v2/futures/add-margin";
            var requestBody = $$"""{"id":"{{id}}","amount":{{amount}}}""";

            return await ExecutePostRequestAsync(key, passphrase, secret, method, path, requestBody, "AddMargin", new object[] { id, amount });
        }

        public async Task<bool> Cancel(string key, string passphrase, string secret, string id)
        {
            var method = "POST";
            var path = "/v2/futures/cancel";
            var requestBody = $"{{\"id\":\"{id}\"}}";

            return await ExecutePostRequestAsync(key, passphrase, secret, method, path, requestBody, "Cancel", new object[] { id });
        }

        public async Task<bool> CreateLimitBuyOrder(string key, string passphrase, string secret, decimal price, decimal takeprofit, int leverage, double quantity)
        {
            var method = "POST";
            var path = "/v2/futures";
            var requestBody = $$"""{"side":"b","type":"l","price":{{price}},"takeprofit":{{takeprofit}},"leverage":{{leverage}},"quantity":{{quantity.ToString(CultureInfo.InvariantCulture)}}}""";

            return await ExecutePostRequestAsync(key, passphrase, secret, method, path, requestBody, "CreateLimitBuyOrder", new object[] { price, takeprofit, leverage, quantity });
        }

        public async Task<bool> CreateNewSwap(string key, string passphrase, string secret)
        {
            var method = "POST";
            var path = "/v2/swap";
            var requestBody = $"{{\"in_asset\":\"BTC\",\"out_asset\":\"USD\",\"in_amount\":{_options.Value.SwapAmount}}}";

            return await ExecutePostRequestAsync(key, passphrase, secret, method, path, requestBody, "CreateNewSwap", new object[] { "BTC", "USD", _options.Value.SwapAmount });
        }

        public async Task<bool> SwapUsdInBtc(string key, string passphrase, string secret, int amount)
        {
            var method = "POST";
            var path = "/v2/swap";
            var requestBody = $$"""{"in_asset":"USD","out_asset":"BTC","in_amount":{{amount}}}""";

            return await ExecutePostRequestAsync(key, passphrase, secret, method, path, requestBody, "SwapUsdInBtc", new object[] { "USD", "BTC", amount });
        }

        public async Task<IEnumerable<FuturesTradeModel>> FuturesGetClosedTradesAsync(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/futures";
            var queryParams = $"type=closed&limit={_options.Value.ClosedTradesLimit}";

            return await ExecuteGetRequestAsync(key, passphrase, secret, method, path, queryParams, "FuturesGetClosedTradesAsync", new List<FuturesTradeModel>());
        }

        public async Task<IEnumerable<FuturesTradeModel>> FuturesGetOpenTradesAsync(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/futures";
            var queryParams = "type=open";

            return await ExecuteGetRequestAsync(key, passphrase, secret, method, path, queryParams, "FuturesGetOpenTradesAsync", new List<FuturesTradeModel>());
        }

        public async Task<IEnumerable<FuturesTradeModel>> FuturesGetRunningTradesAsync(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/futures";
            var queryParams = "type=running";

            return await ExecuteGetRequestAsync(key, passphrase, secret, method, path, queryParams, "FuturesGetRunningTradesAsync", new List<FuturesTradeModel>());
        }

        public async Task<IEnumerable<DepositModel>> GetDeposits(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/user/deposit";
            var queryParams = "";

            return await ExecuteGetRequestAsync(key, passphrase, secret, method, path, queryParams, "GetDeposits", new List<DepositModel>());
        }

        public async Task<UserModel> GetUser(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/user";
            var queryParams = "";

            try
            {
                var data = await ExecuteGetRequestAsync(key, passphrase, secret, method, path, queryParams, "GetUser", (UserModel?)null);
                if (data == null)
                {
                    _logger.LogWarning("GetUser returned null data from LN Markets API");
                    throw new InvalidOperationException("Failed to retrieve user data from LN Markets API");
                }

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while retrieving user data");
                throw;
            }
        }

        public async Task<string> GetWithdrawals(string key, string passphrase, string secret)
        {
            var method = "GET";
            var path = "/v2/user/withdraw";
            var queryParams = "";

            return await ExecuteGetStringRequestAsync(key, passphrase, secret, method, path, queryParams, "GetWithdrawals");
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

        private async Task<bool> ExecutePostRequestAsync(string key, string passphrase, string secret, string method, string path, string requestBody, string operationName, object[]? logParameters = null)
        {
            var timestamp = GetUtcNowInUnixTimestamp();
            var signaturePayload = $"{timestamp}{method}{path}{requestBody}";

            SetLnMarketsHeaders(key, passphrase, GetSignature(secret, signaturePayload), timestamp);
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{_lnMarketsEndpoint}{path}", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    if (logParameters != null)
                    {
                        var sanitizedParams = SanitizeLogParameters(logParameters);
                        _logger.LogDebug($"{operationName} successful for " + string.Join(", ", sanitizedParams.Select((p, i) => $"param{i}: {{{i}}}")), sanitizedParams);
                    }
                    else
                        _logger.LogDebug($"{operationName} successful");
                    return true;
                }

                if (logParameters != null)
                {
                    var sanitizedParams = SanitizeLogParameters(logParameters);
                    _logger.LogWarning($"{operationName} failed for " + string.Join(", ", sanitizedParams.Select((p, i) => $"param{i}: {{{i}}}")) + ". Status: {StatusCode}, Response: {Response}",
                        sanitizedParams.Concat(new object[] { response.StatusCode, SanitizeResponseContent(responseContent) }).ToArray());
                }
                else
                    _logger.LogWarning($"{operationName} failed. Status: {{StatusCode}}, Response: {{Response}}", response.StatusCode, SanitizeResponseContent(responseContent));
                return false;
            }
            catch (Exception ex)
            {
                if (logParameters != null)
                    _logger.LogError(ex, $"Exception occurred while executing {operationName} for " + string.Join(", ", logParameters.Select((p, i) => $"param{i}: {{{i}}}")), logParameters);
                else
                    _logger.LogError(ex, $"Exception occurred while executing {operationName}");
                return false;
            }
            finally
            {
                ClearLnMarketsHeaders();
            }
        }

        private async Task<T> ExecuteGetRequestAsync<T>(string key, string passphrase, string secret, string method, string path, string queryParams, string operationName, T defaultValue) where T : class
        {
            var timestamp = GetUtcNowInUnixTimestamp();
            var signaturePayload = $"{timestamp}{method}{path}{queryParams}";

            SetLnMarketsHeaders(key, passphrase, GetSignature(secret, signaturePayload), timestamp);

            try
            {
                var requestUrl = $"{_lnMarketsEndpoint}{path}?{queryParams}";
                var data = await _httpClient.GetFromJsonAsync<T>(requestUrl) ?? defaultValue;

                if (data is IEnumerable<object> enumerable)
                    _logger.LogDebug($"{operationName} successful, retrieved {{Count}} items", enumerable.Count());
                else
                    _logger.LogDebug($"{operationName} successful");

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception occurred while executing {operationName}");
                return defaultValue;
            }
            finally
            {
                ClearLnMarketsHeaders();
            }
        }

        private async Task<string> ExecuteGetStringRequestAsync(string key, string passphrase, string secret, string method, string path, string queryParams, string operationName)
        {
            var timestamp = GetUtcNowInUnixTimestamp();
            var signaturePayload = $"{timestamp}{method}{path}{queryParams}";

            SetLnMarketsHeaders(key, passphrase, GetSignature(secret, signaturePayload), timestamp);

            try
            {
                var requestUrl = $"{_lnMarketsEndpoint}{path}?{queryParams}";
                var data = await _httpClient.GetStringAsync(requestUrl);
                _logger.LogDebug($"{operationName} successful");
                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception occurred while executing {operationName}");
                throw;
            }
            finally
            {
                ClearLnMarketsHeaders();
            }
        }

        private object[] SanitizeLogParameters(object[] parameters)
        {
            return parameters.Select(p => p is string str && IsCredential(str) ? "***" : p).ToArray();
        }

        private string SanitizeResponseContent(string responseContent)
        {
            // Don't log potentially sensitive response content completely
            return responseContent.Length > 200 ? 
                $"{responseContent[..100]}... [truncated {responseContent.Length - 100} chars]" : 
                responseContent;
        }

        private bool IsCredential(string value)
        {
            // Basic heuristic to detect potential credentials
            return !string.IsNullOrEmpty(value) && (
                value.Length > 20 || // Likely API keys are longer
                value.Contains("sk_") || // Common API key prefix
                value.Contains("pk_") ||
                value.All(char.IsLetterOrDigit) && value.Length > 10
            );
        }

        private string GetSignature(string secret, string payload)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToBase64String(hash);
        }

        private static long GetUtcNowInUnixTimestamp() => (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
    }
