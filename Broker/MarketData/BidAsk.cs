using Broker.Utils;

namespace Broker.MarketData
{
    public class BidAsk : IMarketData
    {
        public double Bid { get; set; }
        public decimal BidSize { get; set; }
        public double Ask { get; set; }
        public decimal AskSize { get; set; }
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
                BidSize = ba.BidSize,
                Ask = ba.Ask,
                AskSize = ba.AskSize,
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
                Time = ba.Time.ToUnixTimeSeconds(),
            };
        }

        public static explicit operator BidAsk(IBApi.HistoricalTickBidAsk ba)
        {
            return new BidAsk()
            {
                Bid = ba.PriceBid,
                BidSize = ba.SizeBid,
                Ask = ba.PriceAsk,
                AskSize = ba.SizeAsk,
                Time = DateTimeOffset.FromUnixTimeSeconds(ba.Time).DateTime.ToLocalTime(),
            };
        }

        public static explicit operator IBApi.HistoricalTickBidAsk(BidAsk ba)
        {
            return new IBApi.HistoricalTickBidAsk(
                ba.Time.ToUnixTimeSeconds(),
                new IBApi.TickAttribBidAsk(),
                ba.Bid,
                ba.Ask,
                ba.BidSize,
                ba.AskSize
            );
        }
    }
}
