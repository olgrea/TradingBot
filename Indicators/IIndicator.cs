using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    public interface IIndicator
    {
        public bool IsReady { get; }
        public int NbPeriods { get; }
        void Update(Bar bar);
        void Reset();
    }
}
