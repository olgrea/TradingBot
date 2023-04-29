using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using NLog;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.MarketData.Providers;
using TradingBotV2.DataStorage.Sqlite.DbCommandFactories;
using TradingBotV2.DataStorage.Sqlite.DbCommands;

namespace TradingBotV2.IBKR
{
    internal class IBHistoricalDataProvider : IHistoricalDataProvider
    {
        public const string DefaultDbPath = @"C:\tradingbot\db\historicaldata.sqlite3";

        class MarketDataCache
        {
            public ConcurrentDictionary<DateTime, IEnumerable<IMarketData>> Cache = new();
            public ConcurrentDictionary<DateTime, byte> TimestampsRetrieved = new();
            
            //debug
            List<DateTime> KeysDebug => Cache.Keys.OrderBy(k => k).ToList();
        }

        ConcurrentDictionary<string, ConcurrentDictionary<Type, MarketDataCache>> _cache = new();

        // for unit tests
        internal int _nbRetrievedFromIBKR = 0;
        internal int _nbRetrievedFromDb = 0;
        internal int _nbRetrievedFromCache = 0;
        internal int _nbInsertedInCache = 0;
        internal int _nbInsertedInDb = 0;
        internal void ClearCache() => _cache.Clear();

        int _nbRequest = 0;
        int NbRequest
        {
            get => _nbRequest;
            set
            {
                _nbRequest = value;
                CheckForPacingViolations();
            }
        }

        IBClient _client;
        IBBroker _broker;
        ILogger? _logger;
        CancellationToken _token;

        public IBHistoricalDataProvider(IBBroker broker, ILogger? logger)
        {
            _broker = broker;
            _client = broker.Client;
            _logger = logger;
        }

        public string DbPath { get; internal set; } = DefaultDbPath;
        public bool DbEnabled { get; set; } = true;
        public bool CacheEnabled { get; set; } = true;
        
        public async Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime date) where TData : IMarketData, new()
        {
            return await GetHistoricalDataAsync<TData>(ticker, date, CancellationToken.None);
        }

        public async Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime from, DateTime to) where TData : IMarketData, new()
        {
            return await GetHistoricalDataAsync<TData>(ticker, from, to, CancellationToken.None);
        }

        public async Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime date, CancellationToken token) where TData : IMarketData, new()
        {
            date = new DateTime(date.Date.Ticks + MarketDataUtils.MarketStartTime.Ticks);

            ValidateDate(date);

            IEnumerable<IMarketData> data = new LinkedList<IMarketData>();
            var progress = new Action<IEnumerable<IMarketData>>(newData =>
            {
                token.ThrowIfCancellationRequested();
                data = newData.Concat(data);
            });

            token.ThrowIfCancellationRequested();
            var marketHours = date.ToMarketHours();
            await GetDataInChunks<TData>(ticker, marketHours.Item1, marketHours.Item2, progress);

            return data;
        }

        public async Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime from, DateTime to, CancellationToken token) where TData : IMarketData, new()
        {
            ValidateDates(from, to);

            IEnumerable<IMarketData> data = new LinkedList<IMarketData>();
            var progress = new Action<IEnumerable<IMarketData>>(newData =>
            {
                token.ThrowIfCancellationRequested();
                data = newData.Concat(data);
            });    

            foreach ((DateTime, DateTime) day in MarketDataUtils.GetMarketDays(from, to))
            {
                token.ThrowIfCancellationRequested();
                await GetDataInChunks<TData>(ticker, day.Item1, day.Item2, progress);
            }

            // TODO : fix this. "from" and "to" time of day are not respected when retrieved from server
            return data.SkipWhile(d => d.Time < from);
        }

        internal async Task GetDataInChunks<TData>(string ticker, DateTime from, DateTime to, Action<IEnumerable<IMarketData>> onChunckReceived) where TData : IMarketData, new()
        {
            Type datatype = typeof(TData);
            _logger?.Info($"Getting {datatype.Name} for {ticker} on {from.Date.ToShortDateString()} ({from.ToShortTimeString()} to {to.ToShortTimeString()})");

            _nbRetrievedFromCache = 0;
            _nbRetrievedFromDb = 0;
            _nbRetrievedFromIBKR = 0;
            _nbInsertedInCache = 0;
            _nbInsertedInDb = 0;

            DateTime current = to;
            DbCommandFactory? cmdFactory = null;
            while (current > from)
            {
                // In order to respect TWS limitations, data is retrieved in chunks, from the end of the
                // time range to the beginning. 
                // https://interactivebrokers.github.io/tws-api/historical_limitations.html
                var chunkEnd = current;
                var chunkBegin = current.AddSeconds(-30 * 60);
                if (chunkBegin < from)
                    chunkBegin = from;

                IEnumerable<IMarketData>? data;
                if (!TryGetFromCache<TData>(ticker, chunkBegin, chunkEnd, out data))
                {
                    if(cmdFactory == null)
                        cmdFactory = DbCommandFactory.Create<TData>(DbPath);

                    if(TryGetFromDb<TData>(ticker, datatype, chunkBegin, chunkEnd, cmdFactory, out data))
                    {
                        InsertInCache<TData>(ticker, chunkBegin, chunkEnd, data);
                    }
                    else
                    {
                        data = await GetFromServer<TData>(ticker, current);
                        _logger?.Info($"{typeof(TData).Name} for {ticker} {current.Date.ToShortDateString()} ({chunkBegin.ToShortTimeString()}-{chunkEnd.ToShortTimeString()}) received from TWS.");
                        if (data != null && data.Any())
                        {
                            InsertInDb<TData>(ticker, data, cmdFactory);
                            InsertInCache<TData>(ticker, chunkBegin, chunkEnd, data);
                        }
                    }
                }
                Debug.Assert(data != null);
                onChunckReceived?.Invoke(data);
                current = current.AddMinutes(-30);
            }
        }

        bool TryGetFromCache<TData>(string ticker, DateTime from, DateTime to, [NotNullWhen(true)] out IEnumerable<IMarketData>? data) where TData : IMarketData, new()
        {
            if (!CacheEnabled || !_cache.TryGetValue(ticker, out var typeCache) || !typeCache.TryGetValue(typeof(TData), out var dataCache))
            {
                data = null;
                return false;
            }

            data = Enumerable.Empty<IMarketData>();
            for(DateTime i = from; i < to; i = i.AddSeconds(1))
            {
                if (!dataCache.TimestampsRetrieved.ContainsKey(i))
                {
                    data = null;
                    return false;
                }

                if(dataCache.Cache.TryGetValue(i, out IEnumerable<IMarketData>? value))
                    data = data.Concat(value);
            }

            _nbRetrievedFromCache += data.Count();
            return true;
        }

        void InsertInCache<TData>(string ticker, DateTime from, DateTime to, IEnumerable<IMarketData> newData) where TData : IMarketData, new()
        {
            if (!CacheEnabled)
                return;

            var typeCache = _cache.GetOrAdd(ticker, new ConcurrentDictionary<Type, MarketDataCache>());
            var dataCache = typeCache.GetOrAdd(typeof(TData), new MarketDataCache());

            foreach (IGrouping<DateTime, IMarketData> data in newData.GroupBy(d => d.Time))
            {
                dataCache.Cache.AddOrUpdate(data.Key, data, (k, currentData) => currentData.Union(data));
            }

            _nbInsertedInCache += newData.Count();
            MarkAsRetrieved<TData>(ticker, from, to);
        }

        void MarkAsRetrieved<TData>(string ticker, DateTime from, DateTime to) where TData : IMarketData, new()
        {
            if (!CacheEnabled)
                return;

            if (from > to)
                return;
            
            if(_cache.TryGetValue(ticker, out var typeCache) && typeCache.TryGetValue(typeof(TData), out var dataCache))
            {
                DateTime current = from;
                while (current < to)
                {
                    dataCache.TimestampsRetrieved.GetOrAdd(current, 0);
                    current = current.AddSeconds(1);
                }
            }
        }

        bool TryGetFromDb<TData>(string ticker, Type datatype, DateTime from, DateTime to, DbCommandFactory cmdFactory, [NotNullWhen(true)] out IEnumerable<IMarketData>? data) where TData : IMarketData, new()
        {
            if(!DbEnabled)
            {
                data = null;
                return false;
            }

            DbCommand<bool> existsCmd = cmdFactory.CreateExistsCommand(ticker, from.Date, (from.TimeOfDay, to.TimeOfDay));
            if(!existsCmd.Execute())
            {
                data = null;
                return false;
            }

            var selectCmd = cmdFactory.CreateSelectCommand(ticker, from.Date, (from.TimeOfDay, to.TimeOfDay));
            data = selectCmd.Execute();
            _nbRetrievedFromDb += data.Count();

            var dateStr = from.Date.ToShortDateString();
            _logger?.Info($"{datatype.Name} for {ticker} {dateStr} ({from.ToShortTimeString()}-{to.ToShortTimeString()}) already exists in db. Skipping.");

            return true;
        }

        void InsertInDb<TData>(string ticker, IEnumerable<IMarketData> data, DbCommandFactory commandFactory) where TData : IMarketData, new()
        {
            if (!DbEnabled)
                return;

            _logger?.Info($"Inserting in db.");
            DbCommand<bool> insertCmd = commandFactory.CreateInsertCommand(ticker, data);
            if (insertCmd.Execute() && insertCmd is InsertCommand<TData> iCmd)
                _nbInsertedInDb += iCmd.NbInserted;
        }

        async Task<IEnumerable<IMarketData>> GetFromServer<TData>(string ticker, DateTime time) where TData : IMarketData, new()
        {
            if (!_broker.IsConnected())
                await _broker.ConnectAsync();

            IEnumerable<IMarketData> data;
            if (typeof(TData) == typeof(Bar))
            {
                data = await FetchBars<TData>(ticker, time);
            }
            else
            {
                data = await FetchTooMuchData<TData>(ticker, time);
            }

            _nbRetrievedFromIBKR += data.Count();
            return data;
        }

        private async Task<IEnumerable<IMarketData>> FetchTooMuchData<TData>(string ticker, DateTime time) where TData : IMarketData, new()
        {
            Debug.Assert(MarketDataUtils.WasMarketOpen(time));

            Type datatype = typeof(TData);
            _logger?.Info($"Retrieving {datatype.Name} from TWS for '{ticker} {time}'.");

            // Max nb of ticks per request is 1000, but since there can be multiple BidAsk per second it's not possible to know how many
            // ticks are needed for 30 minutes.
            // So we just do requests as long as we don't have 30 minutes.
            // Inefficient because we're retrieving more data than we need but it works...
            IEnumerable<IMarketData> data = new LinkedList<IMarketData>();
            DateTime current = time;
            TimeSpan _30min = TimeSpan.FromMinutes(30);
            var diff = time - current;
            int tickCount = 1000;
            while (diff <= _30min)
            {
                IEnumerable<IMarketData> ticks = Enumerable.Empty<IMarketData>();
                if (datatype == typeof(BidAsk))
                {
                    ticks = await GetHistoricalBidAsksAsync(ticker, current, tickCount);
                    // Note that when BID_ASK historical data is requested, each request is counted twice according to the doc
                    NbRequest++; NbRequest++;
                }
                else if (datatype == typeof(Last))
                {
                    ticks = await GetHistoricalLastsAsync(ticker, current, tickCount);
                    NbRequest++;
                }

                data = ticks.Concat(data);
                current = ticks.First().Time;

                diff = time - current;
            }

            // Remove out of range data.
            var timeOfDay = (time - _30min).TimeOfDay;
            return data.SkipWhile(d => d.Time.TimeOfDay < timeOfDay);
        }

        private async Task<IEnumerable<IMarketData>> FetchBars<TData>(string ticker, DateTime time) where TData : IMarketData, new()
        {
            Debug.Assert(MarketDataUtils.WasMarketOpen(time));
            _logger?.Info($"Retrieving bars from TWS for '{ticker} {time}'.");
            var bars = await GetHistoricalBarsAsync(ticker, BarLength._1Sec, time, 1800);
            NbRequest++;
            return bars;
        }

        async Task<IEnumerable<Bar>> GetHistoricalBarsAsync(string ticker, BarLength barLength, DateTime endDateTime, int count)
        {
            var tmpList = new LinkedList<Bar>();
            var tcs = new TaskCompletionSource<IEnumerable<Bar>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reqId = -1;

            var historicalData = new Action<int, IBApi.Bar>((rId, IBApiBar) =>
            {
                if (rId == reqId)
                {
                    var bar = (Bar)IBApiBar;
                    _logger?.Trace($"GetHistoricalDataAsync - historicalData - adding bar {bar.Time}");
                    bar.BarLength = barLength;
                    tmpList.AddLast(bar);
                }
            });

            var historicalDataEnd = new Action<int, string, string>((rId, start, end) =>
            {
                if (rId == reqId)
                {
                    _logger?.Trace($"GetHistoricalDataAsync - historicalDataEnd - setting result");
                    tcs.SetResult(tmpList);
                }
            });

            var error = new Action<ErrorMessage>(msg => tcs.TrySetException(msg));

            //string timeFormat = "yyyyMMdd-HH:mm:ss";

            // Duration         : Allowed Bar Sizes
            // 60 S             : 1 sec - 1 mins
            // 120 S            : 1 sec - 2 mins
            // 1800 S (30 mins) : 1 sec - 30 mins
            // 3600 S (1 hr)    : 5 secs - 1 hr
            // 14400 S (4hr)	: 10 secs - 3 hrs
            // 28800 S (8 hrs)  : 30 secs - 8 hrs
            // 1 D              : 1 min - 1 day
            // 2 D              : 2 mins - 1 day
            // 1 W              : 3 mins - 1 week
            // 1 M              : 30 mins - 1 month
            // 1 Y              : 1 day - 1 month

            string durationStr = string.Empty;
            string barSizeStr = string.Empty; 
            switch (barLength)
            {
                case BarLength._1Sec:
                    durationStr = $"{count} S";
                    barSizeStr = "1 secs";
                    break;

                case BarLength._5Sec:
                    durationStr = $"{5 * count} S";
                    barSizeStr = "5 secs";
                    break;

                case BarLength._1Min:
                    durationStr = $"{60 * count} S";
                    barSizeStr = "1 min";
                    break;

                default:
                    throw new NotImplementedException($"Unable to retrieve historical data for bar lenght {barLength}");
            }

            string edt = endDateTime == DateTime.MinValue ? string.Empty : $"{endDateTime.ToString("yyyyMMdd HH:mm:ss")} US/Eastern";

            _client.Responses.HistoricalData += historicalData;
            _client.Responses.HistoricalDataEnd += historicalDataEnd;
            _client.Responses.Error += error;
            try
            {
                var contract = _client.ContractsCache.Get(ticker);
                reqId = _client.RequestHistoricalData(contract, edt, durationStr, barSizeStr, true);
                await tcs.Task;
            }
            finally
            {
                _client.Responses.HistoricalData -= historicalData;
                _client.Responses.HistoricalDataEnd -= historicalDataEnd;
                _client.Responses.Error -= error;
            }

            return tcs.Task.Result;
        }

        async Task<IEnumerable<BidAsk>> GetHistoricalBidAsksAsync(string ticker, DateTime time, int count)
        {
            CancellationTokenSource source = new CancellationTokenSource(Debugger.IsAttached ? -1 : 15000);

            int reqId = -1;
            var tmpList = new LinkedList<BidAsk>();

            var tcs = new TaskCompletionSource<IEnumerable<BidAsk>>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.Token.Register(() => tcs.TrySetException(new TimeoutException($"GetHistoricalBidAsksAsync")));
            var historicalTicks = new Action<int, IEnumerable<IBApi­.HistoricalTickBidAsk>, bool>((rId, data, isDone) =>
            {
                if (rId == reqId)
                {
                    _logger?.Trace($"GetHistoricalBidAsksAsync - adding {data.Count()}");

                    foreach (var d in data)
                        tmpList.AddLast((BidAsk)d);

                    if (isDone)
                    {
                        _logger?.Trace($"GetHistoricalBidAsksAsync - SetResult");
                        tcs.SetResult(tmpList);
                    }
                }
            });
            var error = new Action<ErrorMessage>(msg => tcs.TrySetException(msg));

            _client.Responses.HistoricalTicksBidAsk += historicalTicks;
            _client.Responses.Error += error;
            try
            {
                var contract = _client.ContractsCache.Get(ticker);
                reqId = _client.RequestHistoricalTicks(contract, string.Empty, $"{time.ToString("yyyyMMdd HH:mm:ss")} US/Eastern", count, "BID_ASK", false, true);
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.HistoricalTicksBidAsk -= historicalTicks;
                _client.Responses.Error -= error;
            }
        }

        async Task<IEnumerable<Last>> GetHistoricalLastsAsync(string ticker, DateTime time, int count)
        {
            CancellationTokenSource source = new CancellationTokenSource(Debugger.IsAttached ? -1 : 15000);

            int reqId = -1;
            var tmpList = new LinkedList<Last>();

            var tcs = new TaskCompletionSource<IEnumerable<Last>>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.Token.Register(() => tcs.TrySetException(new TimeoutException($"GetHistoricalLastsAsync")));
            var historicalTicks = new Action<int, IEnumerable<IBApi­.HistoricalTickLast>, bool>((rId, data, isDone) =>
            {
                if (rId == reqId)
                {
                    _logger?.Trace($"GetHistoricalLastsAsync - adding {data.Count()}");

                    foreach (var d in data)
                        tmpList.AddLast((Last)d);

                    if (isDone)
                    {
                        _logger?.Trace($"GetHistoricalLastsAsync - SetResult");
                        tcs.SetResult(tmpList);
                    }
                }
            });
            var error = new Action<ErrorMessage>(msg => tcs.TrySetException(msg));

            _client.Responses.HistoricalTicksLast += historicalTicks;
            _client.Responses.Error += error;
            try
            {
                var contract = _client.ContractsCache.Get(ticker);
                reqId = _client.RequestHistoricalTicks(contract, string.Empty, $"{time.ToString("yyyyMMdd HH:mm:ss")} US/Eastern", count, "TRADES", false, true);
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.HistoricalTicksLast -= historicalTicks;
                _client.Responses.Error -= error;
            }
        }
        
        private void CheckForPacingViolations()
        {
            // TWS API limitations. Pacing violation occurs when : 
            // - Making identical historical data requests within 15 seconds.
            // - Making six or more historical data requests for the same Contract, Exchange and Tick Type within two seconds.
            // - Making more than 60 requests within any 10 minute period.
            // https://interactivebrokers.github.io/tws-api/historical_limitations.html

            // Not working properly since program restarts are not taken into account...

            if (_nbRequest == 60)
            {
                int minutes = 10;
                _logger?.Info($"60 requests made : waiting {minutes} minutes...");
                WaitFor(minutes);
                _logger?.Info($"Resuming.");
                _nbRequest = 0;
            }
            else if (_nbRequest != 0 && _nbRequest % 5 == 0)
            {
                _logger?.Info($"{NbRequest} requests made : waiting 2 seconds...");
                Task.Delay(2000).Wait();
                _logger?.Info($"Resuming.");
            }
        }

        void WaitFor(int minutes)
        {
            for (int i = 0; i < minutes; ++i)
            {
                Task.Delay(60 * 1000).Wait();
                if (i < minutes-1)
                    _logger?.Info($"{9 - i} minutes left...");
            }
        }

        private static void ValidateDate(DateTime date)
        {
            // https://interactivebrokers.github.io/tws-api/historical_limitations.html
            if (DateTime.Now - date > TimeSpan.FromDays(6 * 30))
                throw new ArgumentException($"Bars whose size is 30 seconds or less older than six months are not available. {date}");
            if (!MarketDataUtils.WasMarketOpen(date))
                throw new ArgumentException($"The market was closed on {date}");
        }

        private static void ValidateDates(DateTime from, DateTime to)
        {
            if (from >= to)
                throw new ArgumentException("Starting date is after ending date");

            IEnumerable<(DateTime, DateTime)> days = MarketDataUtils.GetMarketDays(from, to).ToList();
            if (!days.Any())
                throw new ArgumentException($"Market was closed from {from} to {to}");
        }
    }
}
