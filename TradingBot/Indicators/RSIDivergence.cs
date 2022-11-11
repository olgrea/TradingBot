using System;
using System.Linq;
using System.Collections.Generic;
using Skender.Stock.Indicators;
using TradingBot.Utils;
using InteractiveBrokers.MarketData;

namespace TradingBot.Indicators
{
    public class RsiDivergenceResult : ResultBase
    {
        public double? RSIDivergence { get; set; }
    }

    internal class RsiDivergence : IndicatorBase<RsiDivergenceResult>
    {
        Rsi _slowRsi;
        Rsi _fastRsi;

        public RsiDivergence(BarLength barLength, int slowPeriod=14, int fastPeriod=5) 
            : base(barLength, slowPeriod, 10*slowPeriod)
        {
            _slowRsi = new Rsi(barLength, slowPeriod);
            _fastRsi = new Rsi(barLength, fastPeriod);
        }

        public Rsi SlowRSI => _slowRsi;
        public Rsi FastRSI => _fastRsi;

        public override void Compute(IEnumerable<IQuote> quotes)
        {
            base.Compute(quotes);
            _slowRsi.Compute(quotes);
            _fastRsi.Compute(quotes);
            _results = ComputeResults(_slowRsi.Results, _fastRsi.Results);
        }

        public override void ComputeTrend(Last last)
        {
            base.ComputeTrend(last);
            _slowRsi.ComputeTrend(last);
            _fastRsi.ComputeTrend(last);
            _trendingResults = ComputeResults(_slowRsi.TrendingResults, _fastRsi.TrendingResults);
        }

        IEnumerable<RsiDivergenceResult> ComputeResults(IEnumerable<RsiResult> slowRsiResults, IEnumerable<RsiResult> fastRsiResults)
        {
            return slowRsiResults.Zip(fastRsiResults, (slow, fast) =>
            {
                return new RsiDivergenceResult()
                {
                    RSIDivergence = fast.Rsi - slow.Rsi,
                    Date = fast.Date,
                };
            });
        }
    }
}