using System.Collections.Generic;
using System.Linq;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    internal abstract class IndicatorBase : IIndicator
    {
        LinkedList<Bar> _bars = new LinkedList<Bar>();

        public IndicatorBase(int nbPeriods)
        {
            NbPeriods = nbPeriods;
        }

        public LinkedList<Bar> Bars => _bars;
        public int NbPeriods { get; private set; }
        public virtual bool IsReady => _bars.Count == NbPeriods;

        public void Update(Bar bar)
        {
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
