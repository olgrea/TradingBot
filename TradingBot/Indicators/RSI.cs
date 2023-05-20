using Skender.Stock.Indicators;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    internal enum RsiSignal
    {
        None = 0,
        Overbought,
        Oversold,
    }

    internal class Rsi : IndicatorBase<RsiResult, RsiSignal>
    {
        const double _oversoldThreshold = 30.0;
        const double _overboughtThreshold = 70.0;

        public Rsi(BarLength barLength, int nbPeriods = 14) : base(barLength, nbPeriods, 10*nbPeriods)
        {
        }
        
        protected override IEnumerable<RsiResult> ComputeResults()
        {
            return _quotes.GetRsi(NbPeriods);
        }

        protected override IEnumerable<RsiSignal> ComputeSignals()
        {
            List<RsiSignal> signals = new List<RsiSignal>();
            if(IsReady && _results.Any())
            {
                if (_results.Last().Rsi > _overboughtThreshold)
                    signals.Add(RsiSignal.Overbought);
                else if(_results.Last().Rsi < _oversoldThreshold)
                    signals.Add(RsiSignal.Oversold);
            }

            return signals;
        }
    }
}
