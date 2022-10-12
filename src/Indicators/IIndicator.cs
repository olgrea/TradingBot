using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    public interface IIndicator
    {
        public bool IsReady { get; }
        public int NbPeriods { get; }
        public BarLength BarLength { get; }
        void Update(Bar bar);
    }
}
