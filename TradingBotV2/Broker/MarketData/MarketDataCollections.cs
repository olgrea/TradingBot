namespace TradingBotV2.Broker.MarketData
{
    public class MarketDataCollections
    {
        public IDictionary<DateTime, Bar> Bars { get; set; } = new Dictionary<DateTime, Bar>();
        public IDictionary<DateTime, IEnumerable<BidAsk>> BidAsks { get; set; } = new Dictionary<DateTime, IEnumerable<BidAsk>>();
        public IDictionary<DateTime, IEnumerable<Last>> Lasts { get; set; } = new Dictionary<DateTime, IEnumerable<Last>>();
    }
}
