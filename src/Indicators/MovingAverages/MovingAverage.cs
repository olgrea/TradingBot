using System.Collections;
using System.Collections.Generic;
using System.Linq;

using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators.MovingAverages
{
    internal abstract class MovingAverage : IndicatorBase, IIndicator
    {
        protected double _last = double.MinValue;
        protected double _lastDelta = double.MinValue;

        public MovingAverage(BarLength barLength, int nbPeriods) : base(barLength, nbPeriods) { }

        public double Value { get; protected set; }
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

            Value = ComputeMA();

            Delta = Value - _last;
            DeltaOfDelta = Delta - _lastDelta;
        }

        public abstract double ComputeMA();
    }
}
