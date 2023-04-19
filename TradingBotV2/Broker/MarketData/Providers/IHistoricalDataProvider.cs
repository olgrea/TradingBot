namespace TradingBotV2.Broker.MarketData.Providers
{
    public interface IHistoricalDataProvider
    {
        public Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime date) where TData : IMarketData, new();
        public Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime from, DateTime to) where TData : IMarketData, new();
        public Task GetDataForDayInChunks<TData>(string ticker, DateTime date, (TimeSpan, TimeSpan) timeRange, Action<IEnumerable<IMarketData>> onChunckReceived) where TData : IMarketData, new();
    }
}
