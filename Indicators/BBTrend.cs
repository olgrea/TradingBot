using System;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    public class BBTrend : IIndicator
    {
        BollingerBands _bb20 = new BollingerBands(20);
        BollingerBands _bb50 = new BollingerBands(50);

        public double Value { get; private set; }
        public bool IsReady => _bb50.IsReady;
        public int NbPeriods => _bb50.NbPeriods;

        public void Update(Bar bar)
        {
            _bb20.Update(bar);
            _bb50.Update(bar);
            Compute();
        }

        public void Compute()
        {
            if(!IsReady)
            {
                Value = 0;
                return;
            }

            var lower = Math.Abs(_bb20.LowerBB - _bb50.LowerBB);
            var upper = Math.Abs(_bb20.UpperBB - _bb50.UpperBB);
            Value = (lower - upper) / _bb20.MovingAverage;
        }

        public void Reset()
        {
            _bb20.Reset();
            _bb50.Reset();
        }
    }
}
