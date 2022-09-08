using System;
using System.Collections.Generic;
using System.Text;

using TradingBot.Broker.MarketData;
using MathNet.Numerics.Statistics;
using System.Linq;

namespace TradingBot.Indicators
{
    public class BollingerBands : IIndicator
    {
        public BollingerBands(int nbPeriods = 20)
        {
            MovingAverage = new MovingAverage(nbPeriods);
        }

        public MovingAverage MovingAverage { get; private set; }
        public double UpperBB { get; private set; }
        public double LowerBB { get; private set; }
        LinkedList<Bar> Bars => MovingAverage.Bars;
        public int NbPeriods => MovingAverage.NbPeriods;
        public bool IsReady => MovingAverage.IsReady;

        public void Update(Bar bar)
        {
            MovingAverage.Update(bar);
            Compute();
        }

        public void Compute()
        {
            var barsTypicalPrice = Bars.Select(b => (b.Close + b.High + b.Low) / 3);

            var sdev = barsTypicalPrice.StandardDeviation();

            UpperBB = MovingAverage + 2 * sdev;
            LowerBB = MovingAverage - 2 * sdev;
        }
    }
}
