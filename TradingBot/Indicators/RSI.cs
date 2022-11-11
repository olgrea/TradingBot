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
        
        IEnumerable<IQuote> _quotes;
        IEnumerable<RsiResult> _results;

        LinkedList<IQuote> _partialQuotes = new LinkedList<IQuote>();
        IEnumerable<RsiResult> _trendingResults;

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

        public RsiResult LatestResult => _results?.LastOrDefault();
        public IEnumerable<RsiResult> Results => _results;

        public RsiResult LatestTrendingResult => _trendingResults?.LastOrDefault();
        public IEnumerable<RsiResult> TrendingResults => _trendingResults;

        public BarLength BarLength => _barLength;
        public bool IsReady => LatestResult != null && _results.Count() >= _nbWarmupPeriods;
        public int NbPeriods => _nbPeriods;
        public int NbWarmupPeriods => _nbWarmupPeriods;

        public void Compute(IEnumerable<IQuote> quotes)
        {
            _partialQuotes.Clear();
            _quotes = quotes;
            _results = ComputeResults(_quotes);
        }

        public void ComputeTrend(IQuote partialQuote)
        {
            _partialQuotes.AddLast(partialQuote);
            _trendingResults = ComputeResults(_quotes.Concat(_partialQuotes));
        }

        IEnumerable<RsiResult> ComputeResults(IEnumerable<IQuote> quotes)
        {
            return quotes.GetRsi(_nbPeriods);
        }
    }
}
