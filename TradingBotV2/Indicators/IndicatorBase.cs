using Skender.Stock.Indicators;
using TradingBot.Indicators.Quotes;
using TradingBotV2.Broker.MarketData;

namespace TradingBot.Indicators
{
    internal abstract class IndicatorBase<TResult> : IIndicator where TResult : ResultBase
    {
        BarLength _barLength;
        int _nbPeriods;
        int _nbWarmupPeriods;

        protected IEnumerable<IQuote> _quotes;
        protected IEnumerable<TResult> _results;

        protected LinkedList<Last> _lasts = new LinkedList<Last>();
        protected IQuote _partialQuote;
        protected IEnumerable<TResult> _trendingResults;

        public IndicatorBase(BarLength barLength, int nbPeriods, int nbWarmupPeriods)
        {
            _barLength = barLength;
            _nbPeriods = nbPeriods;
            _nbWarmupPeriods = nbWarmupPeriods;
        }

        public BarLength BarLength => _barLength;
        public int NbPeriods => _nbPeriods;
        public int NbWarmupPeriods => _nbWarmupPeriods;

        public virtual bool IsReady => LatestResult != null && _results.Count() >= NbWarmupPeriods;

        public TResult LatestResult => _results?.LastOrDefault();
        public IEnumerable<TResult> Results => _results;

        public TResult LatestTrendingResult => _trendingResults?.LastOrDefault();
        public IEnumerable<TResult> TrendingResults => _trendingResults;

        public virtual void Compute(IEnumerable<IQuote> quotes)
        {
            _quotes = quotes;
            _lasts.Clear();
        }

        public virtual void ComputePartial(Last last)
        {
            if (!IsReady)
                throw new InvalidOperationException("Indicator is not ready.");

            _lasts.AddLast(last);
            _partialQuote = (BarQuote)MarketDataUtils.MakeBarFromLasts(_lasts, _barLength);
        }
    }
}
