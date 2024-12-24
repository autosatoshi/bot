﻿namespace AutoBot.Services
{
    using AutoBot.Models.LnMarkets;

    public interface ILnMarketsApiService
    {
        Task<IEnumerable<FuturesTradeModel>> FuturesGetRunningTradesAsync(string key, string passphrase, string secret, long from, long to, int limit = 1000);

        Task<IEnumerable<FuturesTradeModel>> FuturesGetOpenTradesAsync(string key, string passphrase, string secret, long from, long to, int limit = 1000);

        Task<IEnumerable<FuturesTradeModel>> FuturesGetClosedTradesAsync(string key, string passphrase, string secret);

        Task<string> GetWithdrawals(string key, string passphrase, string secret);

        Task<IEnumerable<DepositModel>> GetDeposits(string key, string passphrase, string secret);

        Task<UserModel> GetUser(string key, string passphrase, string secret);

        Task<bool> CreateLimitBuyOrder(string key, string passphrase, string secret, decimal price, decimal takeprofit, int leverage, double quantity);

        Task<bool> CreateNewSwap(string key, string passphrase, string secret);

        Task<bool> SwapUsdInBtc(string key, string passphrase, string secret, int amount);

        Task<bool> AddMargin(string key, string passphrase, string secret, string id, decimal amount);

        Task<bool> Cancel(string key, string passphrase, string secret, string id);
    }
}