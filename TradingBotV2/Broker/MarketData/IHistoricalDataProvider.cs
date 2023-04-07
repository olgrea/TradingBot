namespace TradingBotV2.Broker.MarketData
{
    internal interface IHistoricalDataProvider
    {
        public Task<IEnumerable<Bar>> GetHistoricalOneSecBarsAsync(string ticker, DateTime date);
        public Task<IEnumerable<Bar>> GetHistoricalOneSecBarsAsync(string ticker, DateTime from, DateTime to);
        public Task<IEnumerable<BidAsk>> GetHistoricalBidAsksAsync(string ticker, DateTime date);
        public Task<IEnumerable<BidAsk>> GetHistoricalBidAsksAsync(string ticker, DateTime from, DateTime to);
        public Task<IEnumerable<Last>> GetHistoricalLastsAsync(string ticker, DateTime date);
        public Task<IEnumerable<Last>> GetHistoricalLastsAsync(string ticker, DateTime from, DateTime to);
    }
}
