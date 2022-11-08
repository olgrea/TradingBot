using System;
using System.Collections.Generic;
using InteractiveBrokers.MarketData;

namespace Backtester
{
    internal class MarketDataCollections
    {
        public IEnumerable<Bar> Bars { get; set; }
        public IEnumerable<BidAsk> BidAsks { get; set; }
    }
}
