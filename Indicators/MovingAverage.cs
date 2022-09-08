using System.Linq;
using System.Collections.Generic;
using TradingBot.Broker.MarketData;
using MathNet.Numerics.Statistics;

namespace TradingBot.Indicators
{
    //TODO : make some stuff internal
    public class MovingAverage : IIndicator
    {
        double _lastMA = double.MinValue;
        double _lastDeltaMA = double.MinValue;
        LinkedList<Bar> _bars = new LinkedList<Bar>();

        public MovingAverage(int nbPeriods)
        {
            NbPeriods = nbPeriods;
        }

        public int NbPeriods { get; private set; }
        public double MovingAvg { get; private set;}
        public double DeltaMA { get; private set; }
        public double DeltaSpeed { get; private set; }
        public LinkedList<Bar> Bars => _bars;
        public bool IsReady => _bars.Count == NbPeriods;

        public static implicit operator double(MovingAverage ma) => ma.MovingAvg;

        public override string ToString()
        {
            return MovingAvg.ToString();
        }

        public void Update(Bar bar)
        {
            if (_bars.Any() && _bars.Last() == bar)
                return;

            _bars.AddLast(bar);
            if (_bars.Count > NbPeriods)
                _bars.RemoveFirst();

            Compute();
        }

        public void Compute()
        {
            _lastMA = MovingAvg;
            _lastDeltaMA = DeltaMA;
            
            var barsTypicalPrice = _bars.Select(b => (b.Close + b.High + b.Low) / 3);
            MovingAvg = barsTypicalPrice.Mean();

            DeltaMA = _lastMA != double.MinValue ? MovingAvg - _lastMA : double.MinValue;
            DeltaSpeed = (DeltaMA != double.MinValue && _lastDeltaMA != double.MinValue) ? DeltaMA - _lastDeltaMA : double.MinValue;
        }
    }
}
