using System;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Broker.MarketData
{
    public struct Bar
    {
        public Decimal Open { get; set; }
        public Decimal Close { get; set; }
        public Decimal High { get; set; }
        public Decimal Low { get; set; }
        public long Volume { get; set; }
        public int TradeAmount { get; set; }
        public DateTime Time { get; set; }
    }
}
