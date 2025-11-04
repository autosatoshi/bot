using AutoBot.Models.LnMarkets;
using AutoBot.Models.Units;

namespace AutoBot.Services;

public interface IMarketplaceClient
{
    Task<UserModel?> GetUser(string key, string passphrase, string secret);

    Task<IReadOnlyList<FuturesTradeModel>> GetOpenTrades(string key, string passphrase, string secret);

    Task<IReadOnlyList<FuturesTradeModel>> GetRunningTrades(string key, string passphrase, string secret);

    Task<bool> AddMargin(string key, string passphrase, string secret, string tradeId, Satoshi amountInSats);

    Task<bool> SwapUsdToBtc(string key, string passphrase, string secret, int amountInUsd);

    Task<bool> Cancel(string key, string passphrase, string secret, string id);

    Task<bool> CreateLimitBuyOrder(string key, string passphrase, string secret, decimal price, decimal takeprofit, int leverage, double quantity);
}
