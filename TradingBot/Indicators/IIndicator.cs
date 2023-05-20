using Skender.Stock.Indicators;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    public interface IIndicator
    {
        BarLength BarLength { get; }
        bool IsReady { get; }
        int NbPeriods { get; }
        int NbWarmupPeriods { get; }

        // TODO : update this when Skender.Stock.Indicators' streaming feature will be available (v3.0 apparently).
        void Compute(IEnumerable<IQuote> quotes);
    }
}
