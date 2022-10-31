using System.Collections.Generic;
using Skender.Stock.Indicators;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    public interface IIndicator
    {
        public BarLength BarLength { get; }
        public bool IsReady { get; }
        int NbPeriods { get; }
        int NbWarmupPeriods { get; }

        // TODO : support Last
        // TODO : update this when Skender.Stock.Indicators' streaming feature will be available.
        void Compute(IEnumerable<IQuote> quotes);
    }
}
