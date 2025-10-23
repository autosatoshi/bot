using AutoBot.Models.Units;

namespace AutoBot.Models.LnMarkets;

public class FuturesTradeModel
{
    public required string id { get; set; }

    public required string uid { get; set; }

    public required string type { get; set; }

    public required string side { get; set; }

    public Satoshi opening_fee { get; set; }

    public Satoshi closing_fee { get; set; }

    public Satoshi maintenance_margin { get; set; }

    public Dollar quantity { get; set; }

    public Satoshi margin { get; set; }

    public decimal leverage { get; set; }

    public Dollar price { get; set; }

    public Dollar liquidation { get; set; }

    public Dollar stoploss { get; set; }

    public Dollar takeprofit { get; set; }

    public Dollar? exit_price { get; set; }

    public Satoshi pl { get; set; }

    public long creation_ts { get; set; }

    public long? market_filled_ts { get; set; }

    public long? closed_ts { get; set; }

    public bool open { get; set; }

    public bool running { get; set; }

    public bool canceled { get; set; }

    public bool closed { get; set; }

    public long last_update_ts { get; set; }

    public Satoshi sum_carry_fees { get; set; }
}
