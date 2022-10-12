using System;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    internal class BBTrend : IIndicator
    {
        BollingerBands _bb20;
        BollingerBands _bb50;

        public BBTrend(BarLength barLength)
        {
            _bb20 = new BollingerBands(barLength, 20);
            _bb50 = new BollingerBands(barLength, 50);
        }

        public double Value { get; private set; }
        public bool IsReady => _bb50.IsReady;
        public int NbPeriods => _bb50.NbPeriods;
        public BarLength BarLength => _bb50.BarLength;

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
    }
}
