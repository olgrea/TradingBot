using System;
using System.Collections.Concurrent;
using TradingBotV2.Broker;
using TradingBotV2.Broker.MarketData;

namespace TradingBotV2.Backtesting
{
    public class MarketDataCache<TData> where TData : IMarketData, new()
    {
        IBroker _broker;
        Task _backgroundTask;
        ConcurrentDictionary<string, ConcurrentDictionary<DateTime, IEnumerable<TData>>> _marketData = new ConcurrentDictionary<string, ConcurrentDictionary<DateTime, IEnumerable<TData>>>();

        // Used as a hashset
        ConcurrentDictionary<DateTime, byte> _timestampsRetrieved = new ConcurrentDictionary<DateTime, byte>();

        public MarketDataCache(IBroker broker)
        {
            _broker = broker;
        }

        //debug
        List<DateTime> KeysDebug => _marketData.Values.First().Keys.OrderBy(k => k).ToList();

        public async Task<IEnumerable<TData>> GetAsync(string ticker, DateTime dateTime)
        {
            var timeDict = _marketData.GetOrAdd(ticker, new ConcurrentDictionary<DateTime, IEnumerable<TData>>()); 
            if(!timeDict.TryGetValue(dateTime, out IEnumerable<TData> marketData))
            {
                if (_timestampsRetrieved.ContainsKey(dateTime))
                    return Enumerable.Empty<TData>();

                // Retrieving 10 minutes right now and the rest on a background task
                DateTime to = dateTime.AddMinutes(10);
                var data = (await _broker.HistoricalDataProvider.GetHistoricalDataAsync<TData>(ticker, dateTime, to)).Cast<TData>();
                
                FillCache(ticker, data);
                MarkTimestampsAsRetrieved(dateTime, to);

                marketData = data.OrderBy(d => d.Time).TakeWhile(d => d.Time <= dateTime);

                if (_backgroundTask == null)
                {
                    _backgroundTask = Task.Run(async () => 
                    {
                        var progress = new Action<IEnumerable<IMarketData>>(newData =>
                        {
                            MarkTimestampsAsRetrieved(newData.First().Time, newData.Last().Time);
                            FillCache(ticker, newData.Cast<TData>());
                        });
                        await _broker.HistoricalDataProvider.GetDataForDayInChunks<TData>(ticker, dateTime.Date, MarketDataUtils.MarketDayTimeRange, progress);
                    });
                }
            }

            return marketData;
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

        void MarkTimestampsAsRetrieved(DateTime from, DateTime to)
        {
            if (from > to) 
                return;

            DateTime current = from;
            while (current < to)
            {
                _timestampsRetrieved.GetOrAdd(current, 0);
                current = current.AddSeconds(1);
            }
        }
    }
}
