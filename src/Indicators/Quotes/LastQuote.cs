using System;
using InteractiveBrokers.MarketData;
using Skender.Stock.Indicators;

namespace TradingBot.Indicators.Quotes
{
    internal class LastQuote : IQuote
    {
        public LastQuote(Last last)
        {
            Last = last;
        }

        public Last Last { get; private set; }

        public DateTime Date => Last.Time;

        decimal IQuote.Open => Convert.ToDecimal(Last.Price);

        decimal IQuote.High => Convert.ToDecimal(Last.Price);

        decimal IQuote.Low => Convert.ToDecimal(Last.Price);

        decimal IQuote.Close => Convert.ToDecimal(Last.Price);

        decimal IQuote.Volume => Convert.ToDecimal(Last.Size);

        public static implicit operator LastQuote(Last l) => new LastQuote(l);
    }
}
