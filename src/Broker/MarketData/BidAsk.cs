using System;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Broker.MarketData
{
    internal class BidAsk : IMarketData
    {
        public double Bid { get; set; }
        public int BidSize { get; set; }
        public double Ask { get; set; }
        public int AskSize { get; set; }
        public DateTime Time { get; set; }
    }
}
