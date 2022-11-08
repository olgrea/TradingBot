using System;

namespace InteractiveBrokers.MarketData
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
    }
}
