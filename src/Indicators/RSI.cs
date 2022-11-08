using System.Collections.Generic;
using System.Linq;
using InteractiveBrokers.MarketData;
using Skender.Stock.Indicators;

namespace TradingBot.Indicators
{
    public class Rsi : IIndicator
    {
        const double _oversoldThreshold = 30.0;
        const double _overboughtThreshold = 70.0;
        
        IEnumerable<RsiResult> _rsiResults;
        int _nbPeriods;
        int _nbWarmupPeriods;
        BarLength _barLength;

        public Rsi(BarLength barLength, int nbPeriods = 14)
        {
            _barLength = barLength;
            _nbPeriods = nbPeriods;
            _nbWarmupPeriods = 10*nbPeriods;
        }
        
        public bool IsOverbought => IsReady && LatestResult.Rsi > _overboughtThreshold;
        public bool IsOversold => IsReady && LatestResult.Rsi < _oversoldThreshold;
        public RsiResult LatestResult => _rsiResults?.LastOrDefault();
        public IEnumerable<RsiResult> Results => _rsiResults;

        public BarLength BarLength => _barLength;
        public bool IsReady => LatestResult != null && _rsiResults.Count() == _nbWarmupPeriods;
        public int NbPeriods => _nbPeriods;
        public int NbWarmupPeriods => _nbWarmupPeriods;

        public void Compute(IEnumerable<IQuote> quotes)
        {
            _rsiResults = quotes.GetRsi(_nbPeriods);
        }
    }
}
