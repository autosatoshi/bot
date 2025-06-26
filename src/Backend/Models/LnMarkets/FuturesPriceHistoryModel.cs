namespace AutoBot.Models.LnMarkets;

public class FuturesPriceHistoryModel
    {
        public long Time { get; set; }

        public decimal Value { get; set; }

        public DateTime DateTime => DateTimeOffset.FromUnixTimeSeconds(Time / 1000).UtcDateTime;
    }
