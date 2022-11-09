using System.Collections.Generic;
using System.Linq;
using InteractiveBrokers.MarketData;
using Skender.Stock.Indicators;

namespace TradingBot.Indicators
{
    internal class BollingerBands : IIndicator
    {
        IEnumerable<IQuote> _quotes;
        IEnumerable<BollingerBandsResult> _results;

        LinkedList<IQuote> _partialQuotes = new LinkedList<IQuote>();
        IEnumerable<BollingerBandsResult> _trendingResults = Enumerable.Empty<BollingerBandsResult>();

        BarLength _barLength;
        int _nbPeriods;

        public BollingerBands(BarLength barLength, int nbPeriods = 20)
        {
            _barLength = barLength;
            _nbPeriods = nbPeriods;
        }
        
        public BollingerBandsResult LatestResult => _results?.LastOrDefault();
        public IEnumerable<BollingerBandsResult> Results => _results;

        public BollingerBandsResult LatestTrendingResult => _trendingResults?.LastOrDefault();
        public IEnumerable<BollingerBandsResult> TrendingResults => _trendingResults;

        public BarLength BarLength => _barLength;
        public bool IsReady => LatestResult != null && _results.Count() == NbWarmupPeriods;
        public int NbPeriods => _nbPeriods;
        public int NbWarmupPeriods => _nbPeriods;

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

        IEnumerable<BollingerBandsResult> ComputeResults(IEnumerable<IQuote> quotes)
        {
            return quotes.Use(CandlePart.HLC3).GetBollingerBands();
        }
    }
}
