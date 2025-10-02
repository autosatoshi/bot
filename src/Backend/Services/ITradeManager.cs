using AutoBot.Models.LnMarkets;

namespace AutoBot.Services;

public interface ITradeManager
{
    Task HandlePriceUpdateAsync(LastPriceData data);
}
