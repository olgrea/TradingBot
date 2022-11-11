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

    internal class BBTrend : IndicatorBase<BBTrendResult>
    {
        BollingerBands _slowBB;
        BollingerBands _fastBB;

        public BBTrend(BarLength barLength, int slowPeriod = 50, int fastPeriod = 20) : base(barLength, slowPeriod, slowPeriod)
        {
            _slowBB = new BollingerBands(barLength, slowPeriod);
            _fastBB = new BollingerBands(barLength, fastPeriod);
        }
        
        public override void Compute(IEnumerable<IQuote> quotes)
        {
            base.Compute(quotes);
            _slowBB.Compute(quotes);
            _fastBB.Compute(quotes);
            _results = ComputeResults(_slowBB.Results, _fastBB.Results);
        }

        public override void ComputeTrend(Last last)
        {
            base.ComputeTrend(last);
            _slowBB.ComputeTrend(last);
            _fastBB.ComputeTrend(last);
            _trendingResults = ComputeResults(_slowBB.TrendingResults, _fastBB.TrendingResults);
        }

        IEnumerable<BBTrendResult> ComputeResults(IEnumerable<IResult> slowBBResults, IEnumerable<IResult> fastBBResults)
        {
            return slowBBResults.Cast<BollingerBandsResult>().Zip(fastBBResults.Cast<BollingerBandsResult>(), (slow, fast) =>
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
