namespace TradingBotV2.Broker.MarketData
{
    internal interface IHistoricalDataProvider
    {
        internal interface IHistoricalDataProvider
        {
            public Task<IEnumerable<Bar>> GetHistoricalBarsAsync(string ticker, BarLength barLength, DateTime endDateTime, int count);
            public Task<IEnumerable<BidAsk>> GetHistoricalBidAsksAsync(string ticker, DateTime time, int count);
            public Task<IEnumerable<Last>> GetHistoricalLastsAsync(string ticker, DateTime time, int count);
        }
    }
}
