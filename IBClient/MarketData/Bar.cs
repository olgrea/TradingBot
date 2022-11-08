using System;
using Skender.Stock.Indicators;

namespace TradingBot.Broker.MarketData
{
    public enum BarLength
    {
        _1Sec = 1,
        _5Sec = 5,
        _1Min = 60,
        _1Hour = 3600,
        _1Day = 3600 * 24,
    }

    public class Bar : IMarketData, IQuote
    {
        public const string TWSTimeFormat = "yyyyMMdd  HH:mm:ss";

        public double Open { get; set; }
        public double Close { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public long Volume { get; set; }
        public int TradeAmount { get; set; }
        public DateTime Time { get; set; }
        public BarLength BarLength { get; set; }

        public DateTime Date => Time;

        decimal IQuote.Open => Convert.ToDecimal(Open);

        decimal IQuote.High => Convert.ToDecimal(High);

        decimal IQuote.Low => Convert.ToDecimal(Low);

        decimal IQuote.Close => Convert.ToDecimal(Close);

        decimal IQuote.Volume => Convert.ToDecimal(Volume);

        public override bool Equals(object obj)
        {
            var bar = obj as Bar;
            if (obj == null)
                return false;
            return Time.Equals(bar.Time);
        }

        public override int GetHashCode()
        {
            return Time.GetHashCode();
        }

        public override string ToString()
        {
            return $"{Time} : O={Open:c} H={High:c} L={Low:c} C={Close:c}";
        }
    }
}
