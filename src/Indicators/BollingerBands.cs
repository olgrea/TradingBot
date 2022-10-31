using System.Collections.Generic;
using System.Linq;
using Skender.Stock.Indicators;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    internal class BollingerBands : IIndicator
    {
        IEnumerable<BollingerBandsResult> _bbResults;

        BarLength _barLength;
        int _nbPeriods;

        public BollingerBands(BarLength barLength, int nbPeriods = 20)
        {
            _barLength = barLength;
            _nbPeriods = nbPeriods;
        }
        
        public BollingerBandsResult LatestResult => _bbResults?.LastOrDefault();
        public IEnumerable<BollingerBandsResult> Results => _bbResults;

        public BarLength BarLength => _barLength;
        public bool IsReady => LatestResult != null && _bbResults.Count() == NbWarmupPeriods;
        public int NbPeriods => _nbPeriods;
        public int NbWarmupPeriods => _nbPeriods;

        public void Compute(IEnumerable<IQuote> quotes)
        {
            _bbResults = quotes.Use(CandlePart.HLC3).GetBollingerBands();
        }
    }
}
