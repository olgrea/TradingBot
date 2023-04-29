namespace TradingBotV2.Broker.MarketData.Providers
{
    public interface IHistoricalDataProvider
    {
        public Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateOnly date) where TData : IMarketData, new();
        public Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime dateTime) where TData : IMarketData, new();
        public Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime from, DateTime to) where TData : IMarketData, new();
        public Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateOnly date, CancellationToken token) where TData : IMarketData, new();
        public Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime dateTime, CancellationToken token) where TData : IMarketData, new();
        public Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime from, DateTime to, CancellationToken token) where TData : IMarketData, new();
    }
}
