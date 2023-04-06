using System;

namespace TradingBotV2.Broker.MarketData
{
    public enum BarLength
    {
        _1Sec = 1,
        _5Sec = 5,
        _1Min = 60,
        _5min = 5*60,
    }

    public class Bar : IMarketData
    {
        public double Open { get; set; }
        public double Close { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public long Volume { get; set; }
        public int TradeAmount { get; set; }
        public double VWAP { get; set; }
        public DateTime Time { get; set; }
        public BarLength BarLength { get; set; }

        public DateTime Date => Time;

        public override string ToString()
        {
            return $"{Time} : O={Open:c} H={High:c} L={Low:c} C={Close:c}";
        }
    }
}
