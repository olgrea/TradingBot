using System;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Broker.MarketData
{
    public enum BarLength
    {
        FiveSec,
        ThirtySec,
        OneMinute
    }

    public class Bar
    {
        public Decimal Open { get; set; }
        public Decimal Close { get; set; }
        public Decimal High { get; set; }
        public Decimal Low { get; set; }
        public long Volume { get; set; }
        public int TradeAmount { get; set; }
        public DateTime Time { get; set; }
        public BarLength BarLength { get; set; }

        public override bool Equals(object obj)
        {
            var bar = obj as Bar;
            if (obj == null)
                return false;
            return Time.Equals(bar.Time);
        }
    }
}
