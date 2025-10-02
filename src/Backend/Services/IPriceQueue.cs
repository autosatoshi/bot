using AutoBot.Models.LnMarkets;

namespace AutoBot.Services;

public interface IPriceQueue
{
    void UpdatePrice(LastPriceData data);
}
