using System.Globalization;
using TradingBotV2.IBKR.Client;

namespace TradingBotV2.Broker.MarketData
{
    public enum BarLength
    {
        _1Sec = 1,
        _5Sec = 5,
        _1Min = 60,
        _5min = 5 * 60,
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

        public override string ToString()
        {
            return $"{Time} : O={Open:c} H={High:c} L={Low:c} C={Close:c}";
        }

        public static explicit operator IBApi.FiveSecBar(Bar bar)
        {
            return new IBApi.FiveSecBar()
            {
                Open = bar.Open,
                Close = bar.Close,
                High = bar.High,
                Low = bar.Low,
                Volume = bar.Volume,
                WAP = bar.VWAP,
                TradeAmount = bar.TradeAmount,
                Date = new DateTimeOffset(bar.Time).ToUnixTimeSeconds()
            };
        }

        public static explicit operator Bar(IBApi.FiveSecBar bar)
        {
            return new Bar()
            {
                BarLength = BarLength._5Sec,
                Open = bar.Open,
                Close = bar.Close,
                High = bar.High,
                Low = bar.Low,
                Volume = bar.Volume,
                VWAP = bar.WAP,
                TradeAmount = bar.TradeAmount,
                Time = DateTimeOffset.FromUnixTimeSeconds(bar.Date).DateTime.ToLocalTime(),
            };
        }

        public static explicit operator Bar(IBApi.Bar bar)
        {
            return new Bar()
            {
                Open = bar.Open,
                Close = bar.Close,
                High = bar.High,
                Low = bar.Low,
                Volume = bar.Volume,
                TradeAmount = bar.Count,
                Time = DateTime.SpecifyKind(DateTime.ParseExact(bar.Time, MarketDataUtils.TWSTimeFormat, CultureInfo.InvariantCulture), DateTimeKind.Local)
            };
        }
    }
}
