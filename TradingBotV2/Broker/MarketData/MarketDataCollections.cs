using System.Collections.Generic;
using System.Linq;

namespace TradingBotV2.Broker.MarketData
{
    public class MarketDataCollections
    {
        public IEnumerable<Bar> Bars { get; set; } = Enumerable.Empty<Bar>();
        public IEnumerable<BidAsk> BidAsks { get; set; } = Enumerable.Empty<BidAsk>();
        public IEnumerable<Last> Lasts { get; set; } = Enumerable.Empty<Last>();
    }
}
