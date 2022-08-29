using System;
using System.Collections.Generic;
using System.Text;

using TradingBot.Broker.MarketData;
using MathNet.Numerics.Statistics;
using System.Linq;

namespace TradingBot.Strategies
{
    public class BollingerBands
    {
        const int nbPeriods = 20;

        public Decimal MovingAverage { get; private set; }
        public Decimal UpperBB { get; private set; }
        public Decimal LowerBB { get; private set; }

        LinkedList<Bar> _bars = new LinkedList<Bar>();

        public bool IsReady => _bars.Count == nbPeriods;

        public void Update(Bar bar)
        {
            // TODO : make sure that metrics are still valid when bars are not of the same length

            _bars.AddLast(bar);
            if(_bars.Count > nbPeriods)
                _bars.RemoveFirst();

            Compute();
        }

        void Compute()
        {
            var barsTypicalPrice = _bars.Select(b => Convert.ToDouble( (b.Close + b.High + b.Low)/3  ));
            
            MovingAverage = Convert.ToDecimal(Statistics.Mean(barsTypicalPrice));
            var sdev = Convert.ToDecimal(Statistics.StandardDeviation(barsTypicalPrice));

            UpperBB = MovingAverage + 2 * sdev;
            LowerBB = MovingAverage - 2 * sdev;
        }
    }
}
