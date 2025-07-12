namespace AutoBot.Models.LnMarkets;

public class FuturesPriceHistoryModel
    {
        public long Time { get; set; }

        public decimal Value { get; set; }

        public DateTime DateTime => DateTimeOffset.FromUnixTimeSeconds(Time / Constants.DivisorForTimeCalculation).UtcDateTime;

        private static class Constants
        {
            public const int DivisorForTimeCalculation = 1000;
        }
    }
