using System;
using System.Collections.Generic;
using System.Text;

using TradingBot.Broker.MarketData;
using MathNet.Numerics.Statistics;
using System.Linq;

namespace TradingBot.Indicators
{
    public class BollingerBands
    {
        const int nbPeriods = 20;

        public double MovingAverage { get; private set; }
        public double UpperBB { get; private set; }
        public double LowerBB { get; private set; }

        LinkedList<Bar> _bars = new LinkedList<Bar>();

        public bool IsReady => _bars.Count == nbPeriods;

        public void Update(Bar bar)
        {
            // TODO : make sure that metrics are still valid when/if bars are not of the same length

            _bars.AddLast(bar);
            if (_bars.Count > nbPeriods)
                _bars.RemoveFirst();

            Compute();
        }

        public void Compute()
        {
            var barsTypicalPrice = _bars.Select(b => (b.Close + b.High + b.Low) / 3);

            MovingAverage = barsTypicalPrice.Mean();
            var sdev = barsTypicalPrice.StandardDeviation();

            UpperBB = MovingAverage + 2 * sdev;
            LowerBB = MovingAverage - 2 * sdev;
        }
    }
}
