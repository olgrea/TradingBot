using Broker.MarketData;
using Skender.Stock.Indicators;
using Trader.Indicators;

namespace TradingBot.Strategies.Private.Indicators
{
    internal enum SuperTrendSignal
    {
        CrossedLowerBandDownward,
        CrossedLowerBandUpward,
        CrossedUpperBandDownward,
        CrossedUpperBandUpward,
    }

    internal class SuperTrend : IndicatorBase<SuperTrendResult, SuperTrendSignal>
    {
        int _multiplier;

        // long supertrend : nbPeriods = 20, multiplier = 4
        // short supertrend : nbPeriods = 10, multiplier = 2
        public SuperTrend(BarLength barLength, int nbPeriods=10, int multiplier=2) : base(barLength, nbPeriods, nbPeriods+100)
        {
            _multiplier = multiplier;
        }

        //  Convergence warning: the line segment before the first reversal and the first N+100 periods are
        //  unreliable due to an initial guess of trend direction and precision convergence for the underlying ATR values.
        protected override IEnumerable<SuperTrendResult> ComputeResults()
        {
            return _quotes.GetSuperTrend(NbPeriods, _multiplier);
        }

        protected override IEnumerable<SuperTrendSignal> ComputeSignals()
        {
            List<SuperTrendSignal> signals = new();
            if (IsReady && _results.Count() > 1 && _quotes.Count() > 1)
            {
                var latestResult = _results.Last();
                var latestClose = _quotes.Last().Close;
                var previousClose = _quotes.TakeLast(1).Last().Close;

                if(latestResult.LowerBand.HasValue && previousClose >= latestResult.LowerBand && latestClose < latestResult.LowerBand)
                {
                    signals.Add(SuperTrendSignal.CrossedLowerBandDownward);
                }
                else if (latestResult.LowerBand.HasValue && previousClose <= latestResult.LowerBand && latestClose > latestResult.LowerBand)
                {
                    signals.Add(SuperTrendSignal.CrossedLowerBandUpward);
                }
                else if (latestResult.UpperBand.HasValue && previousClose >= latestResult.UpperBand && latestClose < latestResult.UpperBand)
                {
                    signals.Add(SuperTrendSignal.CrossedUpperBandDownward);
                }
                else if (latestResult.UpperBand.HasValue && previousClose <= latestResult.UpperBand && latestClose > latestResult.UpperBand)
                {
                    signals.Add(SuperTrendSignal.CrossedUpperBandUpward);
                }
            }

            return signals;
        }
    }
}
