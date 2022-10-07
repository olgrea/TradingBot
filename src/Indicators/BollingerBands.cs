using MathNet.Numerics.Statistics;
using System.Linq;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    internal class BollingerBands : IndicatorBase
    {
        public BollingerBands(BarLength barLength, int nbPeriods = 20) : base(barLength, nbPeriods) { }

        public double MovingAverage { get; private set; }
        public double UpperBB { get; private set; }
        public double LowerBB { get; private set; }
        public Bar LatestBar => Bars.Last.Value;

        public override void Compute()
        {
            var barsTypicalPrice = Bars.Select(b => (b.Close + b.High + b.Low) / 3);

            var sdev = barsTypicalPrice.StandardDeviation();

            MovingAverage = barsTypicalPrice.Mean();
            UpperBB = MovingAverage + 2 * sdev;
            LowerBB = MovingAverage - 2 * sdev;
        }
    }
}
