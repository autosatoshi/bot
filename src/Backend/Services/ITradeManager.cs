using AutoBot.Models.LnMarkets;

namespace AutoBot.Services;

public interface ITradeManager
{
    public void UpdatePrice(LastPriceData data);
}
