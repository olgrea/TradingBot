using Skender.Stock.Indicators;
using TradingBotV2.Broker.MarketData;

namespace TradingBot.Indicators
{
    internal class BollingerBands : IndicatorBase<BollingerBandsResult>
    {
        public BollingerBands(BarLength barLength, int nbPeriods = 20) : base(barLength, nbPeriods, nbPeriods)
        {
        }

        public override void Compute(IEnumerable<IQuote> quotes)
        {
            base.Compute(quotes);
            _results = ComputeResults(quotes);
        }

        public override void ComputePartial(Last last)
        {
            base.ComputePartial(last);
            _trendingResults = ComputeResults(_quotes.Append(_partialQuote));
        }

        IEnumerable<BollingerBandsResult> ComputeResults(IEnumerable<IQuote> quotes)
        {
            return quotes.Use(CandlePart.HLC3).GetBollingerBands();
        }
    }
}
