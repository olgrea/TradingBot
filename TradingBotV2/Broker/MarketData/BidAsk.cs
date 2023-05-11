namespace TradingBotV2.Broker.MarketData
{
    public class BidAsk : IMarketData
    {
        public double Bid { get; set; }
        public int BidSize { get; set; }
        public double Ask { get; set; }
        public int AskSize { get; set; }
        public DateTime Time { get; set; }

        public override string ToString()
        {
            return $"{Time} : Bid={Bid:c}, Ask={Ask:c}";
        }

        public static explicit operator BidAsk(IBApi.BidAsk ba)
        {
            return new BidAsk()
            {
                Bid = ba.Bid,
                // TODO : update db schema. Loss of data is acceptable for now.
                BidSize = Convert.ToInt32(ba.BidSize),
                Ask = ba.Ask,
                AskSize = Convert.ToInt32(ba.AskSize),
                Time = DateTimeOffset.FromUnixTimeSeconds(ba.Time).DateTime.ToLocalTime(),
            };
        }

        public static explicit operator IBApi.BidAsk(BidAsk ba)
        {
            return new IBApi.BidAsk()
            {
                Bid = ba.Bid,
                BidSize = ba.BidSize,
                Ask = ba.Ask,
                AskSize = ba.AskSize,
                Time = new DateTimeOffset(ba.Time).ToUnixTimeSeconds(),
            };
        }

        public static explicit operator BidAsk(IBApi.HistoricalTickBidAsk ba)
        {
            return new BidAsk()
            {
                Bid = ba.PriceBid,
                BidSize = Convert.ToInt32(ba.SizeBid),
                Ask = ba.PriceAsk,
                AskSize = Convert.ToInt32(ba.SizeAsk),
                Time = DateTimeOffset.FromUnixTimeSeconds(ba.Time).DateTime.ToLocalTime(),
            };
        }

        public static explicit operator IBApi.HistoricalTickBidAsk(BidAsk ba)
        {
            return new IBApi.HistoricalTickBidAsk(
                new DateTimeOffset(ba.Time).ToUnixTimeSeconds(),
                new IBApi.TickAttribBidAsk(),
                ba.Bid,
                ba.Ask,
                ba.BidSize,
                ba.AskSize
            );
        }
    }
}
