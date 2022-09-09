using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingBot.Indicators
{
    public class RSI : IndicatorBase
    {
        const double _oversoldThreshold = 20.0;
        const double _overboughtThreshold = 80.0;

        LinkedList<double> _diffs = new LinkedList<double>();

        public RSI(int nbPeriods) : base(nbPeriods) { }

        public double Value { get; protected set; }
        public bool IsOverbought => Value > _overboughtThreshold;
        public bool IsOversold => Value < _oversoldThreshold;

        public override void Compute()
        {
            if (Bars.Count < 2)
                return;

            // https://www.macroption.com/rsi-calculation/
            UpdateBarToBarChange();
            var avgU = _diffs.Sum(d => Math.Max(d, 0)) / NbPeriods;
            var avgD = _diffs.Sum(d => -Math.Min(-d, 0)) / NbPeriods;
            var RS = avgU / avgD;
            
            Value = 100 - 100/(1+RS);
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
