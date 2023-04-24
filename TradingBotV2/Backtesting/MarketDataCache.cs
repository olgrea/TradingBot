using System.Collections.Concurrent;
using TradingBotV2.Broker;
using TradingBotV2.Broker.MarketData;

namespace TradingBotV2.Backtesting
{
    public class MarketDataCache<TData> where TData : IMarketData, new()
    {
        IBroker _broker;
        Task _backgroundTask;

        public MarketDataCache(IBroker broker)
        {
            _broker = broker;
        }

        ConcurrentDictionary<string, ConcurrentDictionary<DateTime, IEnumerable<TData>>> _marketData { get; set; } = new ConcurrentDictionary<string, ConcurrentDictionary<DateTime, IEnumerable<TData>>>();

        public IDictionary<DateTime, IEnumerable<TData>> this[string ticker] => _marketData[ticker];

        public IEnumerable<TData> this[string ticker, DateTime dateTime] => _marketData[ticker][dateTime];

        public async Task<IEnumerable<TData>> GetAsync(string ticker, DateTime dateTime)
        {
            var timeDict = _marketData.GetOrAdd(ticker, new ConcurrentDictionary<DateTime, IEnumerable<TData>>()); 
            if(!timeDict.TryGetValue(dateTime, out IEnumerable<TData> data))
            {
                // Retrieving 10 minutes right now and the rest on a background task
                DateTime to = dateTime.AddMinutes(10);
                data = (await _broker.HistoricalDataProvider.GetHistoricalDataAsync<TData>(ticker, dateTime, to)).Cast<TData>();
                FillCache(ticker, data);

                if (_backgroundTask == null)
                {
                    _backgroundTask = Task.Run(async () => 
                    {
                        var progress = new Action<IEnumerable<IMarketData>>(newData => FillCache(ticker, newData.Cast<TData>()));
                        await _broker.HistoricalDataProvider.GetDataForDayInChunks<TData>(ticker, dateTime.Date, MarketDataUtils.MarketDayTimeRange, progress);
                    });
                }
            }

            return data;
        }

        void FillCache(string ticker, IEnumerable<TData> newData)
        {
            // TODO : lock instead of concurrent dict?
            var timeDict = _marketData.GetOrAdd(ticker, new ConcurrentDictionary<DateTime, IEnumerable<TData>>());
            foreach (IGrouping<DateTime, TData> data in newData.GroupBy(d => d.Time))
            {
                timeDict.AddOrUpdate(data.Key, data, (k, currentData) => currentData.Union(data));
            }
        }
    }
}
