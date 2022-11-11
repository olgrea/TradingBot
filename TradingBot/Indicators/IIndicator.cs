using System.Collections.Generic;
using InteractiveBrokers.MarketData;
using Skender.Stock.Indicators;

namespace TradingBot.Indicators
{
    public interface IIndicator
    {
        BarLength BarLength { get; }
        bool IsReady { get; }
        int NbPeriods { get; }
        int NbWarmupPeriods { get; }

        // TODO : update this when Skender.Stock.Indicators' streaming feature will be available.
        void Compute(IEnumerable<IQuote> quotes);
        void ComputeTrend(Last last);
    }
}
