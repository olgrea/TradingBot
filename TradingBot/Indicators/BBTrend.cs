using System.Collections.Generic;
using System.Linq;
using InteractiveBrokers.MarketData;
using Skender.Stock.Indicators;

namespace TradingBot.Indicators
{
    public class BBTrendResult : ResultBase
    {
        public double? BBTrend { get; set; }
    }

    public class BBTrend : IIndicator
    {
        IEnumerable<BBTrendResult> _results;
        IEnumerable<BBTrendResult> _trendingResults = Enumerable.Empty<BBTrendResult>();

        BarLength _barLength;
        BollingerBands _slowBB;
        BollingerBands _fastBB;

        public BBTrend(BarLength barLength, int slowPeriod = 50, int fastPeriod = 20)
        {
            _barLength = barLength;
            _slowBB = new BollingerBands(barLength, slowPeriod);
            _fastBB = new BollingerBands(barLength, fastPeriod);
        }
        
        public BBTrendResult LatestResult => _results?.LastOrDefault();

        public BarLength BarLength => _barLength;
        public bool IsReady => LatestResult != null && _results.Count() >= _slowBB.NbWarmupPeriods;
        public int NbPeriods => _slowBB.NbPeriods;
        public int NbWarmupPeriods => _slowBB.NbWarmupPeriods;

        public void Compute(IEnumerable<IQuote> quotes)
        {
            _slowBB.Compute(quotes);
            _fastBB.Compute(quotes);
            _results = ComputeResults(_slowBB.Results, _fastBB.Results);
        }

        public void ComputeTrend(IQuote partialQuote)
        {
            _slowBB.ComputeTrend(partialQuote);
            _fastBB.ComputeTrend(partialQuote);
            _trendingResults = ComputeResults(_slowBB.TrendingResults, _fastBB.TrendingResults);
        }

        IEnumerable<BBTrendResult> ComputeResults(IEnumerable<BollingerBandsResult> slowBBResults, IEnumerable<BollingerBandsResult> fastBBResults)
        {
            return slowBBResults.Zip(fastBBResults, (slow, fast) =>
            {
                var lower = NullMath.Abs(fast.LowerBand - slow.LowerBand);
                var upper = NullMath.Abs(fast.UpperBand - slow.UpperBand);
                return new BBTrendResult()
                {
                    BBTrend = (lower - upper) / slow.Sma,
                    Date = slow.Date
                };
            });
        }
    }
}
