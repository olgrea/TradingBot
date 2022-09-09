using System.Collections.Generic;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    internal class ExpMovingAverage : MovingAverage
    {
        const int Smoothing = 2;
        double _ema = double.MinValue;

        public ExpMovingAverage(int nbPeriods = 14) : base(nbPeriods) {}
        public override bool IsReady => _ema != double.MinValue && base.IsReady;

        public override void Compute()
        {
            if(!base.IsReady)
            {
                // We need an SMA to start computing the EMA.
                base.Compute();
                return;
            }

            _last = _ema == double.MinValue ? Value : _ema;
            _lastDelta = Delta;

            var latestBar = Bars.Last.Value;
            var valToday = (latestBar.Close + latestBar.High + latestBar.Low) / 3;
            var smoothing = Smoothing / (1 + NbPeriods);

            Value = _ema = ComputeEMA(valToday, _last, smoothing);
                
            Delta = Value - _last;
            DeltaOfDelta = Delta - _lastDelta;
        }

        public static double ComputeEMA(double current, double last, double smoothingFactor)
        {
            return current * smoothingFactor + last * (1 - smoothingFactor);
        }
    }
}
