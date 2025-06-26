namespace AutoBot.Models.LnMarkets;

public class DepositModel
    {
        public string id { get; set; }

        public int amount { get; set; }

        public bool success { get; set; }

        public string from_username { get; set; }

        public long ts { get; set; }

        public string type { get; set; }
    }
