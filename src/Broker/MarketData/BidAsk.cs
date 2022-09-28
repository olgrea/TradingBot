using System;

namespace TradingBot.Broker.MarketData
{
    internal class BidAsk : IMarketData
    {
        public double Bid { get; set; }
        public int BidSize { get; set; }
        public double Ask { get; set; }
        public int AskSize { get; set; }
        public DateTime Time { get; set; }

        public override string ToString()
        {
            return $"{Time} : B={Bid:c} x{BidSize}, A={Ask:c} x{AskSize}";
        }
    }
}
