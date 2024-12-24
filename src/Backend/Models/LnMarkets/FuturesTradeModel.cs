namespace AutoBot.Models.LnMarkets
{
    public class FuturesTradeModel
    {
        public string id { get; set; }

        public string uid { get; set; }

        public string type { get; set; }

        public string side { get; set; }

        public decimal opening_fee { get; set; }

        public decimal closing_fee { get; set; }

        public decimal maintenance_margin { get; set; }

        public decimal quantity { get; set; }

        public decimal margin { get; set; }

        public decimal leverage { get; set; }

        public decimal price { get; set; }

        public decimal liquidation { get; set; }

        public decimal stoploss { get; set; }

        public decimal takeprofit { get; set; }

        public decimal exit_price { get; set; }

        public decimal pl { get; set; }

        public long creation_ts { get; set; }

        public long market_filled_ts { get; set; }

        public long closed_ts { get; set; }

        public bool open { get; set; }

        public bool running { get; set; }

        public bool canceled { get; set; }

        public bool closed { get; set; }

        public long last_update_ts { get; set; }

        public decimal sum_carry_fees { get; set; }
    }
}
