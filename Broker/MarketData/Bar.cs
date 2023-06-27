using System.Globalization;
using Broker.IBKR.Client;
using Broker.Utils;
using Skender.Stock.Indicators;

namespace Broker.MarketData
{
    public enum BarLength
    {
        _1Sec = 1,
        _5Sec = 5,
        _1Min = 60,
        _5min = 5 * 60,
    }

    public class Bar : IMarketData, IQuote
    {
        public double Open { get; set; }
        public double Close { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public decimal Volume { get; set; }
        public int NbTrades { get; set; }
        public decimal WAP { get; set; }
        public DateTime Time { get; set; }
        public BarLength BarLength { get; set; }

        decimal IQuote.Open => Convert.ToDecimal(Open);

        decimal IQuote.High => Convert.ToDecimal(High);

        decimal IQuote.Low => Convert.ToDecimal(Low);

        decimal IQuote.Close => Convert.ToDecimal(Close);

        decimal IQuote.Volume => Convert.ToDecimal(Volume);

        DateTime ISeries.Date => Time;

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
                WAP = bar.WAP,
                TradeAmount = bar.NbTrades,
                Date = bar.Time.ToUnixTimeSeconds()
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
                WAP = bar.WAP,
                NbTrades = bar.TradeAmount,
                Time = DateTimeOffset.FromUnixTimeSeconds(bar.Date).DateTime.ToLocalTime(),
            };
        }

        public static explicit operator Bar(IBApi.Bar bar)
        {
            // Non .NET supported timezone string... : "yyyyMMdd HH:mm:ss TimeZoneString"
            // ex : "20230510 09:30:00 America/New_York"
            // But when retrieving an IBApi.Bar from historicalData(), it uses the timezone selected when loggin into TWS
            // so it will always be local
            var time = bar.Time.Substring(0, bar.Time.Length - bar.Time.LastIndexOf(' '));

            return new Bar()
            {
                Open = bar.Open,
                Close = bar.Close,
                High = bar.High,
                Low = bar.Low,
                Volume = bar.Volume,
                NbTrades = bar.Count,
                Time = DateTime.SpecifyKind(DateTime.ParseExact(time, MarketDataUtils.TWSTimeFormat, CultureInfo.InvariantCulture), DateTimeKind.Local)
            };
        }
    }
}
