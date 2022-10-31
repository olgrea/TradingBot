using System.Collections.Generic;
using System.Linq;
using Skender.Stock.Indicators;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    public class BBTrendResult : ResultBase
    {
        public double? BBTrend { get; set; }
    }

    public class BBTrend : IIndicator
    {
        IEnumerable<BBTrendResult> _bbTrendResults;
        
        BarLength _barLength;
        BollingerBands _slowBB;
        BollingerBands _fastBB;

        public BBTrend(BarLength barLength, int slowPeriod = 50, int fastPeriod = 20)
        {
            _barLength = barLength;
            _slowBB = new BollingerBands(barLength, slowPeriod);
            _fastBB = new BollingerBands(barLength, fastPeriod);
        }
        
        public BBTrendResult LatestResult => _bbTrendResults?.LastOrDefault();

        public BarLength BarLength => _barLength;
        public bool IsReady => LatestResult != null && _bbTrendResults.Count() == _slowBB.NbWarmupPeriods;
        public int NbPeriods => _slowBB.NbPeriods;
        public int NbWarmupPeriods => _slowBB.NbWarmupPeriods;

        public void Compute(IEnumerable<IQuote> quotes)
        {
            _slowBB.Compute(quotes);
            _fastBB.Compute(quotes);

            _bbTrendResults = _slowBB.Results.Zip(_fastBB.Results, (slow, fast) =>
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
