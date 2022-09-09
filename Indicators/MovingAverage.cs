using System.Linq;
using MathNet.Numerics.Statistics;

namespace TradingBot.Indicators
{
    //TODO : make some stuff internal
    public class MovingAverage : IndicatorBase, IIndicator
    {
        protected double _last = double.MinValue;
        protected double _lastDelta = double.MinValue;

        public MovingAverage(int nbPeriods) : base(nbPeriods) { }

        public double Value { get; protected set;}
        public double Delta { get; protected set; }
        public double DeltaOfDelta { get; protected set; }

        public static implicit operator double(MovingAverage ma) => ma.Value;

        public override string ToString()
        {
            return Value.ToString();
        }

        public override void Compute()
        {
            _last = Value;
            _lastDelta = Delta;

            var barsTypicalPrice = Bars.Select(b => (b.Close + b.High + b.Low) / 3);
            Value = barsTypicalPrice.Mean();

            Delta = Value - _last;
            DeltaOfDelta = Delta - _lastDelta;
        }
    }
}
