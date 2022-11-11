using System;
using InteractiveBrokers.MarketData;
using Skender.Stock.Indicators;

namespace TradingBot.Indicators.Quotes
{
    internal class BarQuote : IQuote
    {
        public BarQuote() { }

        public BarQuote(Bar bar)
        {
            Bar = bar;
        }

        public Bar Bar { get; private set; }

        public DateTime Date => Bar.Date;

        decimal IQuote.Open => Convert.ToDecimal(Bar.Open);

        decimal IQuote.High => Convert.ToDecimal(Bar.High);

        decimal IQuote.Low => Convert.ToDecimal(Bar.Low);

        decimal IQuote.Close => Convert.ToDecimal(Bar.Close);

        decimal IQuote.Volume => Convert.ToDecimal(Bar.Volume);

        public static explicit operator BarQuote(Bar b) => new BarQuote(b);
    }
}
