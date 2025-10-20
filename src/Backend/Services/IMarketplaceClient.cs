using AutoBot.Models.LnMarkets;
using AutoBot.Models.Units;

namespace AutoBot.Services;

public interface IMarketplaceClient
{
    Task<UserModel?> GetUser(string key, string passphrase, string secret);

    Task<IReadOnlyList<FuturesTradeModel>> GetRunningTrades(string key, string passphrase, string secret);

    Task<bool> AddMargin(string key, string passphrase, string secret, string tradeId, long amountInSats);

    Task<bool> SwapUsdToBtc(string key, string passphrase, string secret, int amountInUsd);

    Task<bool> CreateNewTrade(string key, string passphrase, string secret, decimal exitPriceInUsd, int leverage, double quantityInUsd);
}
