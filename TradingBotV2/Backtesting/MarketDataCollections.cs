using TradingBotV2.Broker;
using TradingBotV2.Broker.MarketData;

namespace TradingBotV2.Backtesting
{
    public class MarketDataCollections
    {
        public MarketDataCollections(IBroker broker) 
        {
            Bars = new MarketDataCache<Bar>(broker);
            BidAsks = new MarketDataCache<BidAsk>(broker);
            Lasts = new MarketDataCache<Last>(broker);
        }

        public MarketDataCache<Bar> Bars { get; init; }
        public MarketDataCache<BidAsk> BidAsks { get; init; }
        public MarketDataCache<Last> Lasts { get; init; }
    }
}
