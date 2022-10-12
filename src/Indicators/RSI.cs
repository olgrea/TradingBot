using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MathNet.Numerics;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    internal class RSI : IndicatorBase
    {
        const double _oversoldThreshold = 30.0;
        const double _overboughtThreshold = 70.0;

        double _lastAvgU = double.MinValue;
        double _lastAvgD = double.MinValue;

        LinkedList<double> _diffs = new LinkedList<double>();
        LinkedList<(DateTime, double)> _values = new LinkedList<(DateTime, double)>();
        LinkedList<(DateTime, double)> _valuesMA = new LinkedList<(DateTime, double)>();

        public RSI(BarLength barLength, int nbPeriods) : base(barLength, nbPeriods) { }

        public double Value { get; protected set; } = double.MinValue;
        public bool IsOverbought => Value > _overboughtThreshold;
        public bool IsOversold => Value < _oversoldThreshold;
        public bool IsUnderRMA => _values.Any() && _valuesMA.Any() && _values.Last().Item2 < _valuesMA.Last().Item2;
        public bool IsOverRMA => _values.Any() && _valuesMA.Any() && _values.Last().Item2 > _valuesMA.Last().Item2;

        public override bool IsReady => base.IsReady && Value != double.MinValue;

        public override void Compute()
        {
            if (Bars.Count < 2)
                return;

            // https://www.macroption.com/rsi-calculation/
            UpdateBarToBarChange();
            if (!base.IsReady)
                return;

            Debug.Assert(_diffs.Count == NbPeriods - 1);
            var upMoves = _diffs.Select(d => Math.Max(d, 0));
            var downMoves = _diffs.Select(d => -1 * Math.Min(d, 0));
            
            if (_lastAvgD == double.MinValue)
            {
                _lastAvgD = downMoves.Sum() / NbPeriods;
                _lastAvgU = upMoves.Sum() / NbPeriods;
            }
            else
            {
                // smoothed moving average (SMA or RMA)
                var avgU = RMA(upMoves.Last(), _lastAvgU);
                var avgD = RMA(downMoves.Last(), _lastAvgD);

                if (avgD.AlmostEqual(0.0))
                    Value = 100.0;
                else if (avgU.AlmostEqual(0.0))
                    Value = 100.0;
                else
                    Value = 100 - (100.0 / (1 + (avgU / avgD)));

                AddValue(Bars.Last.Value.Time, Value);

                _lastAvgD = avgD;
                _lastAvgU = avgU;
            }
        }

        double RMA(double current, double previous)
        {
            var a = 1.0 / NbPeriods;
            return a * current + (1.0 - a) * previous;
        }

        void AddValue(DateTime time, double value)
        {
            _values.AddLast((time, value));
            if (_values.Count > NbPeriods)
                _values.RemoveFirst();

            var newVal = _valuesMA.Any() ? RMA(value, _valuesMA.Last().Item2) : value;
            _valuesMA.AddLast((time, newVal));
            if (_valuesMA.Count > NbPeriods)
                _valuesMA.RemoveFirst();
        }

        void UpdateBarToBarChange()
        {
            var latest = Bars.Last.Value.Close;
            var previous = Bars.Last.Previous.Value.Close;
            _diffs.AddLast(latest - previous);
            if (_diffs.Count > NbPeriods - 1)
                _diffs.RemoveFirst();
        }
    }
}
