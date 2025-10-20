using AutoBot.Models.LnMarkets;

namespace AutoBot.Services;

public interface IMarketplaceClient
{
    Task<IReadOnlyList<FuturesTradeModel>> GetRunningTrades(string key, string passphrase, string secret);

    Task<IReadOnlyList<FuturesTradeModel>> GetOpenTrades(string key, string passphrase, string secret);

    Task<UserModel> GetUser(string key, string passphrase, string secret);

    Task<bool> CreateLimitBuyOrder(string key, string passphrase, string secret, decimal price, decimal takeprofit, int leverage, double quantity);

    Task<bool> CreateNewTrade(string key, string passphrase, string secret, decimal takeprofit, int leverage, double quantity);

    Task<bool> SwapUsdInBtc(string key, string passphrase, string secret, int amount);

    Task<bool> AddMarginInSats(string key, string passphrase, string secret, string id, long amountInSats);

    Task<bool> Cancel(string key, string passphrase, string secret, string id);
}
