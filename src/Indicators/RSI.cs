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

        public RSI(BarLength barLength, int nbPeriods) : base(barLength, nbPeriods) { }

        public double Value { get; protected set; } = double.MinValue;
        public bool IsOverbought => Value > _overboughtThreshold;
        public bool IsOversold => Value < _oversoldThreshold;
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
                // smoothed moving average (SMA)
                var a = 1.0 / NbPeriods;
                var avgU = a * upMoves.Last() + (1.0 - a) * _lastAvgU;
                var avgD = a * downMoves.Last() + (1.0 - a) * _lastAvgD;

                if (avgD.AlmostEqual(0.0))
                    Value = 100.0;
                else if (avgU.AlmostEqual(0.0))
                    Value = 100.0;
                else
                    Value = 100 - (100.0 / (1 + (avgU / avgD)));

                _lastAvgD = avgD;
                _lastAvgU = avgU;
            }
        }

        public override void Reset()
        {
            _diffs.Clear();
            base.Reset();
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
