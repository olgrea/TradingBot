using System;
using System.Collections.Generic;
using TradingBot.Broker.MarketData;
using TradingBot.Utils;

namespace TradingBot.Indicators
{
    internal class RSIDivergence : IIndicator
    {
        RSI _slowRsi;
        RSI _fastRsi;
        LinkedListWithMaxSize<(DateTime, double)> _values;

        public RSIDivergence(BarLength barLength, int slowPeriod=14, int fastPeriod=5)
        {
            _slowRsi = new RSI(barLength, slowPeriod);
            _fastRsi = new RSI(barLength, fastPeriod);

            _values = new LinkedListWithMaxSize<(DateTime, double)>(fastPeriod);
        }

        public double Value => _fastRsi.Value - _slowRsi.Value;
        public bool IsReady => _slowRsi.IsReady && _fastRsi.IsReady;
        public BarLength BarLength => _slowRsi.BarLength;
        public int NbPeriods => _slowRsi.NbPeriods;
        public int NbPeriodsWithConvergence => _slowRsi.NbPeriodsWithConvergence;
        public Bar LatestBar => _fastRsi.Bars.Last.Value;

        public RSI SlowRSI => _slowRsi;
        public RSI FastRSI => _fastRsi;

        public void Update(Bar bar)
        {
            _slowRsi.Update(bar);
            _fastRsi.Update(bar);
            _values.Add((bar.Time, Value));
        }
    }
}
