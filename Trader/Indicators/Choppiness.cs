using Broker.MarketData;
using Skender.Stock.Indicators;
using Trader.Indicators;

namespace TradingBot.Private.Indicators
{
    internal enum ChopSignal
    {
        TrendingBullish, TrendingBearish, Sideway
    }

    internal class Choppiness : IndicatorBase<ChopResult, ChopSignal>
    {
        int _lookbackPeriod;

        public Choppiness(BarLength barLength, int lookbackPeriod=14) : base(barLength, lookbackPeriod, lookbackPeriod+ 1)
        {
            _lookbackPeriod = lookbackPeriod;
        }

        protected override IEnumerable<ChopResult> ComputeResults()
        {
            return _quotes.GetChop(_lookbackPeriod);
        }

        protected override IEnumerable<ChopSignal> ComputeSignals()
        {
            throw new NotImplementedException();
        }
    }
}
