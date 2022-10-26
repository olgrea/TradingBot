using System;
using System.Collections.Generic;
using System.Linq;
using TradingBot.Broker.MarketData;
using TradingBot.Utils;

namespace TradingBot.Indicators
{
    internal abstract class IndicatorBase : IIndicator
    {
        LinkedListWithMaxSize<Bar> _bars;

        public IndicatorBase(BarLength barLength, int nbPeriodsWithConvergence)
        {
            NbPeriodsWithConvergence = nbPeriodsWithConvergence;
            BarLength = barLength;
            
            _bars = new LinkedListWithMaxSize<Bar>(NbPeriodsWithConvergence);
        }

        // TODO : remove bars from indicators?
        public LinkedList<Bar> Bars => _bars;
        public BarLength BarLength { get; private set; }
        public virtual int NbPeriods => NbPeriodsWithConvergence;
        public int NbPeriodsWithConvergence { get; private set; }
        public virtual bool IsReady => _bars.Count == NbPeriodsWithConvergence;

        public void Update(Bar bar)
        {
            if (BarLength != bar.BarLength)
                throw new ArgumentException($"This indicator only supports bar of length {BarLength}");

            if (_bars.Any() && _bars.Last() == bar)
                return;

            _bars.Add(bar);

            Compute();
        }

        public abstract void Compute();
    }
}
