using Skender.Stock.Indicators;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    internal abstract class IndicatorBase<TResult, TSignal> : IIndicator where TResult : ResultBase
    {
        protected BarLength _barLength;
        protected int _nbPeriods;
        protected int _nbWarmupPeriods;

        protected IEnumerable<IQuote> _quotes = Enumerable.Empty<IQuote>();
        protected IEnumerable<TResult> _results = Enumerable.Empty<TResult>();
        protected IEnumerable<TSignal> _signals = Enumerable.Empty<TSignal>();

        public IndicatorBase(BarLength barLength, int nbPeriods, int nbWarmupPeriods)
        {
            _barLength = barLength;
            _nbPeriods = nbPeriods;
            _nbWarmupPeriods = nbWarmupPeriods;
        }

        public BarLength BarLength => _barLength;
        public int NbPeriods => _nbPeriods;
        public int NbWarmupPeriods => _nbWarmupPeriods;
        public virtual bool IsReady => _results.Count() >= NbWarmupPeriods && _results.Last() != null;

        public IEnumerable<TResult> Results => _results;
        public IEnumerable<TSignal> Signals => _signals;

        public void Compute(IEnumerable<IQuote> quotes)
        {
            _quotes = quotes;
            _results = ComputeResults();
            _signals = ComputeSignals();
        }

        protected abstract IEnumerable<TResult> ComputeResults();

        protected abstract IEnumerable<TSignal> ComputeSignals();
    }
}
