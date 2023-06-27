using Broker.MarketData;
using Skender.Stock.Indicators;

namespace Trader.Indicators
{
    internal enum BollingerBandsSignals
    {
        CrossedUpperBandUpward,
        CrossedUpperBandDownward,
        CrossedLowerBandUpward,
        CrossedLowerBandDownward,
    }

    // https://dotnet.stockindicators.dev/indicators/BollingerBands/
    internal class BollingerBands : IndicatorBase<BollingerBandsResult, BollingerBandsSignals>
    {
        double _stDev;
        public BollingerBands(BarLength barLength, int nbPeriods = 20, double stDev = 2.0) : base(barLength, nbPeriods, nbPeriods)
        {
            _stDev = stDev;
        }

        protected override IEnumerable<BollingerBandsResult> ComputeResults()
        {
            return _quotes.GetBollingerBands(NbPeriods, _stDev);
        }

        protected override IEnumerable<BollingerBandsSignals> ComputeSignals()
        {
            List<BollingerBandsSignals> signals = new();
            if (IsReady && _results.Count() > 1 && _quotes.Count() > 1)
            {
                var latestResult = _results.Last();
                var latestClose = Convert.ToDouble(_quotes.Last().Close);
                var previousClose = Convert.ToDouble(_quotes.TakeLast(1).Last().Close);

                if (latestResult.LowerBand.HasValue && previousClose >= latestResult.LowerBand && latestClose < latestResult.LowerBand)
                {
                    signals.Add(BollingerBandsSignals.CrossedLowerBandDownward);
                }
                else if (latestResult.LowerBand.HasValue && previousClose <= latestResult.LowerBand && latestClose > latestResult.LowerBand)
                {
                    signals.Add(BollingerBandsSignals.CrossedLowerBandUpward);
                }
                else if (latestResult.UpperBand.HasValue && previousClose >= latestResult.UpperBand && latestClose < latestResult.UpperBand)
                {
                    signals.Add(BollingerBandsSignals.CrossedUpperBandDownward);
                }
                else if (latestResult.UpperBand.HasValue && previousClose <= latestResult.UpperBand && latestClose > latestResult.UpperBand)
                {
                    signals.Add(BollingerBandsSignals.CrossedUpperBandUpward);
                }
            }

            return signals;
        }
    }
}
