using System;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Broker.MarketData
{
    public struct BidAsk
    {
        public Decimal Bid { get; set; }
        public int BidSize { get; set; }
        public Decimal Ask { get; set; }
        public int AskSize { get; set; }
    }
}
