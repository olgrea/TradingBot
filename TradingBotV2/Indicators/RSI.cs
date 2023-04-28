using Skender.Stock.Indicators;
using TradingBotV2.Broker.MarketData;

namespace TradingBot.Indicators
{
    internal class Rsi : IndicatorBase<RsiResult>
    {
        const double _oversoldThreshold = 30.0;
        const double _overboughtThreshold = 70.0;

        public Rsi(BarLength barLength, int nbPeriods = 14) : base(barLength, nbPeriods, 10*nbPeriods)
        {
        }
        
        public bool IsOverbought => IsReady && LatestResult.Rsi > _overboughtThreshold;
        public bool IsOversold => IsReady && LatestResult.Rsi < _oversoldThreshold;

        public override void Compute(IEnumerable<IQuote> quotes)
        {
            base.Compute(quotes);
            _results = ComputeResults(_quotes);
        }

        public override void ComputePartial(Last last)
        {
            base.ComputePartial(last);
            _trendingResults = ComputeResults(_quotes.Append(_partialQuote));
        }

        IEnumerable<RsiResult> ComputeResults(IEnumerable<IQuote> quotes)
        {
            return quotes.GetRsi(NbPeriods);
        }
    }
}
