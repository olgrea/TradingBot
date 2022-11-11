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

    public class RsiDivergence : IIndicator
    {
        Rsi _slowRsi;
        Rsi _fastRsi;

        IEnumerable<RsiDivergenceResult> _results;
        IEnumerable<RsiDivergenceResult> _trendingResults;

        BarLength _barLength;

        public RsiDivergence(BarLength barLength, int slowPeriod=14, int fastPeriod=5)
        {
            _barLength = barLength;
            _slowRsi = new Rsi(barLength, slowPeriod);
            _fastRsi = new Rsi(barLength, fastPeriod);
        }

        public RsiDivergenceResult LatestResult => _results?.LastOrDefault();
        public IEnumerable<RsiDivergenceResult> Results => _results;

        public RsiDivergenceResult LatestTrendingResult => _trendingResults?.LastOrDefault();
        public IEnumerable<RsiDivergenceResult> TrendingResults => _trendingResults;

        public Rsi SlowRSI => _slowRsi;
        public Rsi FastRSI => _fastRsi;
        
        public BarLength BarLength => _barLength;
        public bool IsReady => LatestResult != null && _results.Count() >= NbWarmupPeriods;
        public int NbPeriods => _slowRsi.NbPeriods;
        public int NbWarmupPeriods => _slowRsi.NbWarmupPeriods;

        public void Compute(IEnumerable<IQuote> quotes)
        {
            _slowRsi.Compute(quotes);
            _fastRsi.Compute(quotes);
            _results = ComputeResults(_slowRsi.Results, _fastRsi.Results);
        }

        public void ComputeTrend(IQuote partialQuote)
        {
            _slowRsi.ComputeTrend(partialQuote);
            _fastRsi.ComputeTrend(partialQuote);
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