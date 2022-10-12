using System;
using System.Collections.Generic;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    internal class RSIDivergence : IIndicator
    {
        RSI _slowRsi;
        RSI _fastRsi;
        LinkedList<(DateTime, double)> _values = new LinkedList<(DateTime, double)>();

        public RSIDivergence(BarLength barLength, int slowPeriod=14, int fastPeriod=5)
        {
            _slowRsi = new RSI(barLength, slowPeriod);
            _fastRsi = new RSI(barLength, fastPeriod);
        }

        public double Value => _fastRsi.Value - _slowRsi.Value;
        public bool IsReady => _slowRsi.IsReady && _fastRsi.IsReady;
        public BarLength BarLength => _slowRsi.BarLength;
        public int NbPeriods => _slowRsi.NbPeriods;
        public Bar LatestBar => _fastRsi.Bars.Last.Value;

        public RSI SlowRSI => _slowRsi;
        public RSI FastRSI => _fastRsi;

        public void Update(Bar bar)
        {
            _slowRsi.Update(bar);
            _fastRsi.Update(bar);

            _values.AddLast((bar.Time, Value));
            if (_values.Count > NbPeriods)
                _values.RemoveFirst();
        }
    }
}
