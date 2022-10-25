using MathNet.Numerics.Statistics;
using System.Linq;
using TradingBot.Broker.MarketData;
using Skender.Stock.Indicators;
using System.Collections.Generic;
using TradingBot.Utils;

namespace TradingBot.Indicators
{
    internal class BollingerBands : IndicatorBase
    {
        IEnumerable<BollingerBandsResult> _bbResults;

        public BollingerBands(BarLength barLength, int nbPeriods = 20) : base(barLength, nbPeriods) {}

        public double MovingAverage => _bbResults?.Last().Sma ?? double.MinValue;
        public double UpperBB => _bbResults?.Last().UpperBand ?? double.MinValue;
        public double LowerBB => _bbResults?.Last().LowerBand ?? double.MinValue;  
        public Bar LatestBar => Bars.Last.Value;

        public override void Compute()
        {
            _bbResults = Bars.Use(CandlePart.HLC3).GetBollingerBands();
        }
    }
}
