using System.Collections.Generic;

namespace InteractiveBrokers.MarketData
{
    public class MarketDataCollections
    {
        public IEnumerable<Bar> Bars { get; set; }
        public IEnumerable<BidAsk> BidAsks { get; set; }
        public IEnumerable<Last> Lasts { get; set; }
    }
}
