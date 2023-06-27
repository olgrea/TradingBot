namespace Broker.MarketData.Providers
{
    public interface IHistoricalDataProvider
    {
        /// <summary>
        /// Retrieves market data of the specified type at the specified date, from market opening to closing.
        /// </summary>
        public Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateOnly date) where TData : IMarketData, new();

        /// <summary>
        /// Retrieves 1 second of market data of the specified type at the time specified by the dateTime.
        /// </summary>
        public Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime dateTime) where TData : IMarketData, new();

        /// <summary>
        /// Retrieves market data of the specified type between the two specified dateTime.
        /// </summary>
        public Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime from, DateTime to) where TData : IMarketData, new();

        /// <summary>
        /// Retrieves market data of the specified type at the specified date, from market opening to closing.
        /// </summary>
        public Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateOnly date, CancellationToken token) where TData : IMarketData, new();

        /// <summary>
        /// Retrieves 1 second of market data of the specified type at the time specified by the dateTime.
        /// </summary>
        public Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime dateTime, CancellationToken token) where TData : IMarketData, new();

        /// <summary>
        /// Retrieves market data of the specified type between the two specified dateTime.
        /// </summary>
        public Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime from, DateTime to, CancellationToken token) where TData : IMarketData, new();
    }
}
