using Broker.MarketData;
using Skender.Stock.Indicators;
using Trader.Indicators;

namespace TradingBot.Strategies.Private.Indicators
{
    // https://www.investopedia.com/terms/k/keltnerchannel.asp
    internal enum KeltnerSignal
    {
        CrossedUpperBandUpward,
        CrossedUpperBandDownward,
        CrossedLowerBandUpward,
        CrossedLowerBandDownward,
    }

    internal class KeltnerChannels : IndicatorBase<KeltnerResult, KeltnerSignal>
    {
        int _emaPeriod;
        double _multiplier;
        int _atrPeriod;

        public KeltnerChannels(BarLength barLength, int emaPeriod=20, double multiplier=2.0, int atrPeriod = 10) 
            : base(barLength, Math.Max(emaPeriod, atrPeriod), Math.Max(2* Math.Max(emaPeriod, atrPeriod), Math.Max(emaPeriod, atrPeriod)+100))
        {
            _emaPeriod = emaPeriod;
            _multiplier = multiplier;
            _atrPeriod = atrPeriod; 
        }

        // Convergence warning: The first N+250 periods will have decreasing magnitude, convergence-related precision
        // errors that can be as high as ~5% deviation in indicator values for earlier periods.
        protected override IEnumerable<KeltnerResult> ComputeResults()
        {
            return _quotes.GetKeltner(_emaPeriod, _multiplier, _atrPeriod);
        }

        protected override IEnumerable<KeltnerSignal> ComputeSignals()
        {
            List<KeltnerSignal> signals = new();
            if (IsReady && _results.Count() > 1 && _quotes.Count() > 1)
            {
                var latestResult = _results.Last();
                var latestClose = Convert.ToDouble(_quotes.Last().Close);
                var previousClose = Convert.ToDouble(_quotes.TakeLast(1).Last().Close);

                if (latestResult.LowerBand.HasValue && previousClose >= latestResult.LowerBand && latestClose < latestResult.LowerBand)
                {
                    signals.Add(KeltnerSignal.CrossedLowerBandDownward);
                }
                else if (latestResult.LowerBand.HasValue && previousClose <= latestResult.LowerBand && latestClose > latestResult.LowerBand)
                {
                    signals.Add(KeltnerSignal.CrossedLowerBandUpward);
                }
                else if (latestResult.UpperBand.HasValue && previousClose >= latestResult.UpperBand && latestClose < latestResult.UpperBand)
                {
                    signals.Add(KeltnerSignal.CrossedUpperBandDownward);
                }
                else if (latestResult.UpperBand.HasValue && previousClose <= latestResult.UpperBand && latestClose > latestResult.UpperBand)
                {
                    signals.Add(KeltnerSignal.CrossedUpperBandUpward);
                }
            }

            return signals;
        }
    }
}
