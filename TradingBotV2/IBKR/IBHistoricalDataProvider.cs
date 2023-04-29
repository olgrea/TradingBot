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
            DateTime? _lower;
            DateTime? _higher;
            
            public bool IsRetrieved(DateTime dateTime)
            {
                return _lower != null && _higher != null 
                    && _lower >= dateTime && _higher < dateTime;
            }

            public void MarkAsRetrieved(DateTime from, DateTime to)
            {
                if (_lower == null || from < _lower)
                    _lower = from;
                if (_higher == null || to > _higher)
                    _higher = to;
            }
            
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

        public IBHistoricalDataProvider(IBBroker broker, ILogger? logger)
        {
            _broker = broker;
            _client = broker.Client;
            _logger = logger;
        }

        public string DbPath { get; internal set; } = DefaultDbPath;
        public bool DbEnabled { get; set; } = true;
        public bool CacheEnabled { get; set; } = true;

        public async Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateOnly date) where TData : IMarketData, new()
        {
            return await GetHistoricalDataAsync<TData>(ticker, date, CancellationToken.None);
        }

        public async Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime dateTime) where TData : IMarketData, new()
        {
            return await GetHistoricalDataAsync<TData>(ticker, dateTime, CancellationToken.None);
        }

        public async Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime from, DateTime to) where TData : IMarketData, new()
        {
            return await GetHistoricalDataAsync<TData>(ticker, from, to, CancellationToken.None);
        }

        public async Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateOnly date, CancellationToken token) where TData : IMarketData, new()
        {
            var marketHours = date.ToDateTime(default).ToMarketHours();
            return await GetHistoricalDataAsync<TData>(ticker, marketHours.Item1, marketHours.Item2, token);
        }

        public async Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime dateTime, CancellationToken token) where TData : IMarketData, new()
        {
            return await GetHistoricalDataAsync<TData>(ticker, dateTime, dateTime.AddSeconds(1), token);
        }

        public async Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime from, DateTime to, CancellationToken token) where TData : IMarketData, new()
        {
            ValidateDates(from, to);

            _nbRetrievedFromCache = 0;
            _nbRetrievedFromDb = 0;
            _nbRetrievedFromIBKR = 0;
            _nbInsertedInCache = 0;
            _nbInsertedInDb = 0;

            DbCommandFactory? cmdFactory = null;
            IEnumerable<IMarketData>? data;
            if (!TryGetFromCache<TData>(ticker, from, to, out data))
            {
                token.ThrowIfCancellationRequested();
                if (cmdFactory == null)
                    cmdFactory = DbCommandFactory.Create<TData>(DbPath);

                if (TryGetFromDb<TData>(ticker, from, to, cmdFactory, out data))
                {
                    token.ThrowIfCancellationRequested();
                    InsertInCache<TData>(ticker, from, to, data);
                }
                else
                {
                    data ??= new LinkedList<IMarketData>();
                    var onChunkReceived = new Action<DateTime, DateTime, IEnumerable<IMarketData>>((from, to, newData) =>
                    {
                        data = newData.Concat(data); 
                        token.ThrowIfCancellationRequested();
                        InsertInDb<TData>(ticker, newData, cmdFactory);
                        token.ThrowIfCancellationRequested();
                        InsertInCache<TData>(ticker, from, to, newData);
                    });

                    await GetFromServer<TData>(ticker, from, to, onChunkReceived);
                }
            }

            // TODO : Test if this is fixed . fix this. "from" and "to" time of day are not respected when retrieved from server
            //return data.SkipWhile(d => d.Time < from);
            token.ThrowIfCancellationRequested();
            return data;
        }

        internal async Task GetFromServer<TData>(string ticker, DateTime from, DateTime to, Action<DateTime, DateTime, IEnumerable<IMarketData>> onChunckReceived) where TData : IMarketData, new()
        {
            _logger?.Info($"Getting {typeof(TData).Name} for {ticker} on {from.Date.ToShortDateString()} ({from.ToShortTimeString()} to {to.ToShortTimeString()})");

            if (!_broker.IsConnected())
                await _broker.ConnectAsync();

            DateTime current = to;
            while (current > from)
            {
                // In order to respect TWS limitations, data is retrieved in chunks, from the end of the
                // time range to the beginning. 
                // https://interactivebrokers.github.io/tws-api/historical_limitations.html
                
                int chunkSizeInSec = 30 * 60;
                var chunkBegin = current.AddSeconds(-chunkSizeInSec);
                var chunkEnd = current;
                if (chunkBegin < from)
                    chunkBegin = from;

                IEnumerable<IMarketData> data;
                if (typeof(TData) == typeof(Bar))
                {
                    data = await FetchBars<TData>(ticker, chunkBegin, chunkEnd);
                }
                else
                {
                    data = await FetchTooMuchData<TData>(ticker, chunkBegin, chunkEnd);
                }
                _nbRetrievedFromIBKR += data.Count();

                _logger?.Info($"{typeof(TData).Name} for {ticker} {current.Date.ToShortDateString()} ({chunkBegin.ToShortTimeString()}-{chunkEnd.ToShortTimeString()}) received from TWS.");
                
                Debug.Assert(data != null);
                onChunckReceived?.Invoke(chunkBegin, chunkEnd, data);
                current = current.AddSeconds(-chunkSizeInSec);
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
                if (!dataCache.IsRetrieved(i))
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
                dataCache.MarkAsRetrieved(from, to);
        }

        bool TryGetFromDb<TData>(string ticker, DateTime from, DateTime to, DbCommandFactory cmdFactory, [NotNullWhen(true)] out IEnumerable<IMarketData>? data) where TData : IMarketData, new()
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
            _logger?.Info($"{typeof(TData).Name} for {ticker} {dateStr} ({from.ToShortTimeString()}-{to.ToShortTimeString()}) already exists in db. Skipping.");

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

        async Task<IEnumerable<IMarketData>> FetchTooMuchData<TData>(string ticker, DateTime from, DateTime to) where TData : IMarketData, new()
        {
            _logger?.Info($"Retrieving {typeof(TData).Name} from TWS for '{ticker}' from {from} to {to}.");

            // Max nb of ticks per request is 1000, but since there can be multiple BidAsk/Last per second it's not possible to know how many
            // ticks are needed for the specified timerange.
            // So we just do requests as long as the time range is filled.
            // Inefficient because we're potentially retrieving more data than we need but it works...
            IEnumerable<IMarketData> data = new LinkedList<IMarketData>();
            
            int tickCount = 1000;
            TimeSpan totalRange = to - from;
            TimeSpan rangeRetrieved = TimeSpan.FromTicks(0);
            DateTime current = to;
            while (rangeRetrieved <= totalRange)
            {
                IEnumerable<IMarketData> ticks = Enumerable.Empty<IMarketData>();
                if (typeof(TData) == typeof(BidAsk))
                {
                    ticks = await GetHistoricalBidAsksAsync(ticker, current, tickCount);
                    // Note that when BID_ASK historical data is requested, each request is counted twice according to the doc.
                    NbRequest++; NbRequest++;
                }
                else if (typeof(TData) == typeof(Last))
                {
                    ticks = await GetHistoricalLastsAsync(ticker, current, tickCount);
                    NbRequest++; NbRequest++;
                }

                data = ticks.Concat(data);
                current = ticks.First().Time;
                rangeRetrieved = to - current;
            }

            // Remove out of range data.
            return data.SkipWhile(d => d.Time < from);
        }

        private async Task<IEnumerable<IMarketData>> FetchBars<TData>(string ticker, DateTime from, DateTime to) where TData : IMarketData, new()
        {
            _logger?.Info($"Retrieving bars from TWS for '{ticker}' from {from} to {to}.");

            int nbBars = (int)(to - from).TotalSeconds; 
            var bars = await GetHistoricalBarsAsync(ticker, BarLength._1Sec, to, nbBars);
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

            // https://interactivebrokers.github.io/tws-api/historical_limitations.html
            if (DateTime.Now - from > TimeSpan.FromDays(6 * 30))
                throw new ArgumentException($"Bars whose size is 30 seconds or less older than six months are not available. {from}");

            IEnumerable<(DateTime, DateTime)> days = MarketDataUtils.GetMarketDays(from, to).ToList();
            if (!days.Any())
                throw new ArgumentException($"Market was closed from {from} to {to}");
        }
    }
}
