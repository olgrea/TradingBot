using System;
using System.Collections.Generic;
using System.Linq;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    internal abstract class IndicatorBase : IIndicator
    {
        LinkedList<Bar> _bars = new LinkedList<Bar>();

        public IndicatorBase(BarLength barLength, int nbPeriods)
        {
            NbPeriods = nbPeriods;
            BarLength = barLength;
        }

        public LinkedList<Bar> Bars => _bars;
        public BarLength BarLength { get; private set; }
        public int NbPeriods { get; private set; }
        public virtual bool IsReady => _bars.Count == NbPeriods;

        public void Update(Bar bar)
        {
            if (BarLength != bar.BarLength)
                throw new ArgumentException($"This indicator only supports bar of length {BarLength}");

            if (_bars.Any() && _bars.Last() == bar)
                return;

            _bars.AddLast(bar);
            if (_bars.Count > NbPeriods)
                _bars.RemoveFirst();

            Compute();
        }

        public abstract void Compute();

        public virtual void Reset()
        {
            _bars.Clear();
        }
    }
}
