using Broker.MarketData;
using Skender.Stock.Indicators;
using Trader.Indicators;

namespace TradingBot.Strategies.Private.Indicators
{
    internal enum VwapSignal
    {
        VwapRising,
        VwapDecreasing, 
        VwapNeutral,
    }

    internal class VWAP : IndicatorBase<VwapResult, VwapSignal>
    {
        public VWAP(BarLength barLength) : base(barLength, 1, 1)
        {
        }

        protected override IEnumerable<VwapResult> ComputeResults()
        {
            return _quotes.GetVwap();
        }

        protected override IEnumerable<VwapSignal> ComputeSignals()
        {
            List<VwapSignal> signals = new();
            if (IsReady && _results.Count() > 1 && _quotes.Count() > 1)
            {
                var latestResult = _results.Last();
                var previousResult = _results.SkipLast(1).Last();

                var precision = 0.001;
                if(latestResult.Vwap.HasValue && previousResult.Vwap.HasValue && Math.Abs(latestResult.Vwap.Value - previousResult.Vwap.Value) < precision)
                {
                    signals.Add(VwapSignal.VwapNeutral);
                }
                else if(latestResult.Vwap > previousResult.Vwap)
                {
                    signals.Add(VwapSignal.VwapRising);
                }
                else if (latestResult.Vwap < previousResult.Vwap)
                {
                    signals.Add(VwapSignal.VwapDecreasing);
                }
            }

            return signals;
        }
    }
}
