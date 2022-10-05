using System;
using System.Collections.Generic;
using System.Text;
using TradingBot.Broker.MarketData;

// TODO : encapsulate this ?
using MathNet.Numerics.Statistics;
using System.Linq;

namespace TradingBot.Indicators.MovingAverages
{
    internal class SimpleMovingAverage : MovingAverage
    {
        public SimpleMovingAverage(BarLength barLength, int nbPeriods) : base(barLength, nbPeriods) { }

        public override double ComputeMA()
        {
            var barsTypicalPrice = Bars.Select(b => (b.Close + b.High + b.Low) / 3);
            return barsTypicalPrice.Mean();
        }
    }
}
