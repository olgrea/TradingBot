using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TradingBot.Indicators
{
    public class RSI : IndicatorBase
    {
        const double _oversoldThreshold = 20.0;
        const double _overboughtThreshold = 80.0;

        double _lastAvgU = double.MinValue;
        double _lastAvgD = double.MinValue;

        LinkedList<double> _diffs = new LinkedList<double>();

        public RSI(int nbPeriods) : base(nbPeriods) { }

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
            var downMoves = _diffs.Select(d => -Math.Min(-d, 0));
            
            if (_lastAvgD == double.MinValue)
            {
                _lastAvgD = downMoves.Sum() / NbPeriods;
                _lastAvgU = upMoves.Sum() / NbPeriods;
            }
            else
            {
                var a = 2 / (NbPeriods + 1);
                var avgU = a * _diffs.Last.Value + (1 - a) * _lastAvgU;
                var avgD = a * _diffs.Last.Value + (1 - a) * _lastAvgD;
                var RS = avgU / avgD;
                Value = 100 - 100/(1+RS);

                _lastAvgD = avgD;
                _lastAvgU = avgU;
            }
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
