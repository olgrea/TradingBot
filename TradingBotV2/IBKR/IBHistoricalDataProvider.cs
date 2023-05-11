using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using NLog;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.MarketData.Providers;
using TradingBotV2.DataStorage.Sqlite.DbCommandFactories;
using TradingBotV2.DataStorage.Sqlite.DbCommands;
using TradingBotV2.IBKR.Client;
using TradingBotV2.Utils;

namespace TradingBotV2.IBKR
{
    internal class IBHistoricalDataProvider : IHistoricalDataProvider
    {
        const int RequestTimeoutInMs = 30000;
        public const string DefaultDbPath = @"C:\tradingbot\db\historicaldata.sqlite3";

        class MarketDataCache
        {
            public ConcurrentDictionary<DateTime, IEnumerable<IMarketData>> Cache = new();

            //debug
            List<DateTime> KeysDebug => Cache.Keys.OrderBy(k => k).ToList();
        }

        record struct BarRequest(IBApi.Contract Contract, string EndDateTime, string DurationStr, string BarSizeStr, string WhatToShow);
        record struct TickRequest(IBApi.Contract Contract, string StartDateTime, string EndDateTime, int NbOfTicks, string WhatToShow);

        ConcurrentDictionary<string, ConcurrentDictionary<Type, MarketDataCache>> _cache = new();
        CancellationToken? _token;

        // for unit tests
        internal int _nbRetrievedFromIBKR = 0;
        internal int _nbRetrievedFromDb = 0;
        internal int _nbRetrievedFromCache = 0;
        internal int _nbInsertedInCache = 0;
        internal int _nbInsertedInDb = 0;
        internal void ClearCache() => _cache.Clear();

        PacingViolationChecker _pvc;
        IBClient _client;
        IBBroker _broker;
        ILogger? _logger;
        private string dbPath = DefaultDbPath;

        public IBHistoricalDataProvider(IBBroker broker, ILogger? logger)
        {
            _broker = broker;
            _client = broker.Client;
            _logger = logger;
            _pvc = new PacingViolationChecker(logger);
        }

        internal ILogger? Logger
        {
            get => _logger;
            set
            {
                _logger = value;
                _pvc.Logger = value;
            }
        }

        public string DbPath
        {
            get => dbPath;
            internal set
            {
                if(!File.Exists(value))
                    DbEnabled = false;

                dbPath = value;
            }
        }

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
            _logger?.Debug($"Getting {typeof(TData).Name} for {ticker} on {date.ToShortDateString()} from {MarketDataUtils.MarketStartTime} to {MarketDataUtils.MarketStartTime}");
            var marketHours = date.ToDateTime(default).ToMarketHours();
            return await GetHistoricalDataAsync<TData>(ticker, marketHours.Item1, marketHours.Item2, token);
        }

        public async Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime dateTime, CancellationToken token) where TData : IMarketData, new()
        {
            _logger?.Debug($"Getting {typeof(TData).Name} for {ticker} at {dateTime}");
            return await GetHistoricalDataAsync<TData>(ticker, dateTime, dateTime.AddSeconds(1), token);
        }

        public async Task<IEnumerable<IMarketData>> GetHistoricalDataAsync<TData>(string ticker, DateTime from, DateTime to, CancellationToken token) where TData : IMarketData, new()
        {
            _logger?.Debug($"Getting {typeof(TData).Name} for {ticker} from {from} to {to}");
            _logger?.Trace($"Cache {(CacheEnabled ? "enabled" : "disabled")}. Db {(DbEnabled ? "enabled" : "disabled")}.");

            _token = token;
            _pvc.Token = token;

            _token?.ThrowIfCancellationRequested();
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
                _token?.ThrowIfCancellationRequested();
                if (cmdFactory == null)
                    cmdFactory = DbCommandFactory.Create<TData>(DbPath);

                if (TryGetFromDb<TData>(ticker, from, to, cmdFactory, out data))
                {
                    _token?.ThrowIfCancellationRequested();
                    InsertInCache<TData>(ticker, from, to, data);
                }
                else
                {
                    data ??= new LinkedList<IMarketData>();
                    var onChunkReceived = new Action<DateTime, DateTime, IEnumerable<IMarketData>>((from, to, newData) =>
                    {
                        data = newData.Concat(data);
                        _token?.ThrowIfCancellationRequested();
                        InsertInDb<TData>(ticker, from, to, newData, cmdFactory);
                        _token?.ThrowIfCancellationRequested();
                        InsertInCache<TData>(ticker, from, to, newData);
                    });

                    await GetFromServer<TData>(ticker, from, to, onChunkReceived);
                }
            }

            _token?.ThrowIfCancellationRequested();
            return data;
        }

        bool TryGetFromCache<TData>(string ticker, DateTime from, DateTime to, [NotNullWhen(true)] out IEnumerable<IMarketData>? data) where TData : IMarketData, new()
        {
            _token?.ThrowIfCancellationRequested();
            if (!CacheEnabled || !_cache.TryGetValue(ticker, out var typeCache) || !typeCache.TryGetValue(typeof(TData), out var dataCache))
            {
                data = null;
                return false;
            }

            data = Enumerable.Empty<IMarketData>();
            for (DateTime i = from; i < to; i = i.AddSeconds(1))
            {
                _token?.ThrowIfCancellationRequested();
                if (!dataCache.Cache.TryGetValue(i, out IEnumerable<IMarketData>? value))
                {
                    _logger?.Trace($"Timestamp {i} not in cache. Can't retrieve whole timerange.");
                    data = null;
                    return false;
                }

                _token?.ThrowIfCancellationRequested();
                data = data.Concat(value);
            }

            int count = data.Count();
            _nbRetrievedFromCache += count;
            _logger?.Debug($"Data {typeof(TData).Name} for {ticker} from ({from} to {to} retrieved from cache ({count}).");
            return true;
        }

        void InsertInCache<TData>(string ticker, DateTime from, DateTime to, IEnumerable<IMarketData> newData) where TData : IMarketData, new()
        {
            _token?.ThrowIfCancellationRequested();
            if (!CacheEnabled)
                return;

            var typeCache = _cache.GetOrAdd(ticker, new ConcurrentDictionary<Type, MarketDataCache>());
            var dataCache = typeCache.GetOrAdd(typeof(TData), new MarketDataCache());

            var dataDict = newData
                .GroupBy(d => d.Time)
                .ToDictionary<IGrouping<DateTime, IMarketData>, DateTime, IEnumerable<IMarketData>>(g => g.Key, g => g);

            int nbInserted = 0;
            for (DateTime i = from; i < to; i = i.AddSeconds(1))
            {
                _token?.ThrowIfCancellationRequested();
                if (dataDict.TryGetValue(i, out var data))
                {
                    dataCache.Cache.AddOrUpdate(i,
                        k =>
                        {
                            nbInserted += data.Count();
                            return data;
                        },
                        (k, currentData) =>
                        {
                            var union = currentData.Union(data);
                            nbInserted += union.Count() - data.Count();
                            return union;
                        });
                }
                else
                {
                    dataCache.Cache.AddOrUpdate(i, Enumerable.Empty<IMarketData>(), (k, currentData) => currentData);
                }
            }

            _nbInsertedInCache += nbInserted;
            if(nbInserted > 0)
                _logger?.Debug($"{nbInserted} {typeof(TData).Name} inserted into cache.");
        }

        bool TryGetFromDb<TData>(string ticker, DateTime from, DateTime to, DbCommandFactory cmdFactory, [NotNullWhen(true)] out IEnumerable<IMarketData>? data) where TData : IMarketData, new()
        {
            _token?.ThrowIfCancellationRequested();
            if (!DbEnabled)
            {
                data = null;
                return false;
            }

            DbCommand<bool> existsCmd = cmdFactory.CreateExistsCommand(ticker, from.Date, (from.TimeOfDay, to.TimeOfDay));
            if (!existsCmd.Execute())
            {
                _logger?.Trace($"Data not in db.");
                data = null;
                return false;
            }

            _token?.ThrowIfCancellationRequested();
            var selectCmd = cmdFactory.CreateSelectCommand(ticker, from.Date, (from.TimeOfDay, to.TimeOfDay));
            data = selectCmd.Execute();
            _nbRetrievedFromDb += data.Count();

            var dateStr = from.Date.ToShortDateString();
            _logger?.Debug($"{typeof(TData).Name} for {ticker} {dateStr} ({from}-{to}) retrieved from db.");

            return true;
        }

        void InsertInDb<TData>(string ticker, DateTime from, DateTime to, IEnumerable<IMarketData> data, DbCommandFactory commandFactory) where TData : IMarketData, new()
        {
            if (!DbEnabled)
                return;

            _token?.ThrowIfCancellationRequested();
            DbCommand<bool> insertCmd = commandFactory.CreateInsertCommand(ticker, new TimeRange(from, to), data);
            if (insertCmd.Execute() && insertCmd is InsertCommand<TData> iCmd)
            {
                _nbInsertedInDb += iCmd.NbInserted;
                if(iCmd.NbInserted > 0)
                    _logger?.Debug($"{iCmd.NbInserted} {typeof(TData).Name} inserted into db.");
            }
        }

        async Task GetFromServer<TData>(string ticker, DateTime from, DateTime to, Action<DateTime, DateTime, IEnumerable<IMarketData>> onChunckReceived) where TData : IMarketData, new()
        {
            _token?.ThrowIfCancellationRequested();
            if (!_broker.IsConnected())
            {
                _logger?.Trace($"Connecting to TWS.");
                await _broker.ConnectAsync();
            }

            DateTime current = to;
            while (current > from)
            {
                _token?.ThrowIfCancellationRequested();
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

                _logger?.Trace($"{typeof(TData).Name} for {ticker} {current.Date.ToShortDateString()} ({chunkBegin}-{chunkEnd}) received from TWS.");

                _token?.ThrowIfCancellationRequested();
                Debug.Assert(data != null);

                onChunckReceived?.Invoke(chunkBegin, chunkEnd, data);
                _token?.ThrowIfCancellationRequested();

                current = current.AddSeconds(-chunkSizeInSec);
            }

            _logger?.Debug($"{typeof(TData).Name} for {ticker} from {from} to {to}) retrieved from TWS server.");
        }

        async Task<IEnumerable<IMarketData>> FetchBars<TData>(string ticker, DateTime from, DateTime to) where TData : IMarketData, new()
        {
            int nbBars = (int)(to - from).TotalSeconds;
            return await GetHistoricalBarsAsync(ticker, BarLength._1Sec, to, nbBars);
        }

        async Task<IEnumerable<Bar>> GetHistoricalBarsAsync(string ticker, BarLength barLength, DateTime endDateTime, int count)
        {
            _token?.ThrowIfCancellationRequested();

            var tmpList = new LinkedList<Bar>();
            var tcs = new TaskCompletionSource<IEnumerable<Bar>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reqId = -1;

            var historicalData = new Action<int, IBApi.Bar>((rId, IBApiBar) =>
            {
                if (_token != null && _token.HasValue && _token.Value.IsCancellationRequested)
                    tcs.TrySetCanceled();

                if (rId == reqId)
                {
                    var bar = (Bar)IBApiBar;
                    bar.BarLength = barLength;
                    tmpList.AddLast(bar);
                }
            });

            var historicalDataEnd = new Action<int, string, string>((rId, start, end) =>
            {
                if (_token != null && _token.HasValue && _token.Value.IsCancellationRequested)
                    tcs.TrySetCanceled();

                if (rId == reqId)
                    tcs.TrySetResult(tmpList);
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

            _logger?.Trace($"Retrieving {count} bars from TWS for '{ticker}' descending from {endDateTime}.");
            var contract = _client.ContractsCache.Get(ticker);
            BarRequest req = new(contract, edt, durationStr, barSizeStr, "TRADES");
            _pvc.CheckRequest(req);

            _client.Responses.HistoricalData += historicalData;
            _client.Responses.HistoricalDataEnd += historicalDataEnd;
            _client.Responses.Error += error;
            try
            {
                reqId = _client.RequestHistoricalData(contract, edt, durationStr, barSizeStr, "TRADES", false);
                _pvc.NbRequest++;

                await tcs.Task;

                if (tcs.Task.IsFaulted || tcs.Task.IsCanceled)
                    _client.CancelHistoricalData(reqId);
                _token?.ThrowIfCancellationRequested();
            }
            finally
            {
                _client.Responses.HistoricalData -= historicalData;
                _client.Responses.HistoricalDataEnd -= historicalDataEnd;
                _client.Responses.Error -= error;
            }

            return tcs.Task.Result;
        }

        async Task<IEnumerable<IMarketData>> FetchTooMuchData<TData>(string ticker, DateTime from, DateTime to) where TData : IMarketData, new()
        {
            // Max nb of ticks per request is 1000, but since there can be multiple BidAsk/Last per second it's not possible to know how many
            // ticks are needed for the specified timerange.
            // So we just do requests as long as the time range is filled.
            // Inefficient because we're potentially retrieving more data than we need but it works...
            IEnumerable<IMarketData> data = new LinkedList<IMarketData>();

            int tickCount = 1000;
            TimeSpan totalRange = to - from;
            TimeSpan rangeRetrieved = TimeSpan.FromTicks(0);
            DateTime current = to;
            while (rangeRetrieved < totalRange)
            {
                _token?.ThrowIfCancellationRequested();

                IEnumerable<IMarketData> ticks = Enumerable.Empty<IMarketData>();
                if (typeof(TData) == typeof(BidAsk))
                {
                    ticks = await GetHistoricalBidAsksAsync(ticker, current, tickCount);
                }
                else if (typeof(TData) == typeof(Last))
                {
                    ticks = await GetHistoricalLastsAsync(ticker, current, tickCount);
                }

                data = ticks.Concat(data);
                if (ticks.Any())
                    current = ticks.First().Time;
                else
                    current = current.AddSeconds(-1);
                rangeRetrieved = to - current;
            }

            // Remove out of range data.
            return data.SkipWhile(d => d.Time < from);
        }

        async Task<IEnumerable<BidAsk>> GetHistoricalBidAsksAsync(string ticker, DateTime time, int count)
        {
            _token?.ThrowIfCancellationRequested();

            CancellationTokenSource source = new CancellationTokenSource(Debugger.IsAttached ? -1 : RequestTimeoutInMs);

            int reqId = -1;
            var tmpList = new LinkedList<BidAsk>();

            var tcs = new TaskCompletionSource<IEnumerable<BidAsk>>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.Token.Register(() => tcs.TrySetException(new TimeoutException($"GetHistoricalBidAsksAsync")));
            var historicalTicks = new Action<int, IEnumerable<IBApi­.HistoricalTickBidAsk>, bool>((rId, data, isDone) =>
            {
                if (_token != null && _token.HasValue && _token.Value.IsCancellationRequested)
                    tcs.TrySetCanceled();

                if (rId == reqId)
                {
                    foreach (var d in data)
                    {
                        if (_token != null && _token.HasValue && _token.Value.IsCancellationRequested)
                            tcs.TrySetCanceled();

                        tmpList.AddLast((BidAsk)d);
                    }

                    if (isDone)
                        tcs.TrySetResult(tmpList);
                }
            });
            var error = new Action<ErrorMessage>(msg => tcs.TrySetException(msg));

            _logger?.Trace($"Retrieving {count} Bid/Ask from TWS for '{ticker}' descending from {time}.");
            var contract = _client.ContractsCache.Get(ticker);
            TickRequest req = new(contract, string.Empty, $"{time.ToString("yyyyMMdd HH:mm:ss")} US/Eastern", count, "BID_ASK");
            _pvc.CheckRequest(req);

            _client.Responses.HistoricalTicksBidAsk += historicalTicks;
            _client.Responses.Error += error;
            try
            {
                reqId = _client.RequestHistoricalTicks(contract, string.Empty, $"{time.ToString("yyyyMMdd HH:mm:ss")} US/Eastern", count, "BID_ASK", false, true);
                // Note that when BID_ASK historical data is requested, each request is counted twice according to the doc.
                _pvc.NbRequest++; 
                _pvc.NbRequest++;
                await tcs.Task;

                if (tcs.Task.IsFaulted || tcs.Task.IsCanceled)
                    _client.CancelHistoricalData(reqId);
                _token?.ThrowIfCancellationRequested();
            }
            finally
            {
                _client.Responses.HistoricalTicksBidAsk -= historicalTicks;
                _client.Responses.Error -= error;
            }

            return tcs.Task.Result;
        }

        async Task<IEnumerable<Last>> GetHistoricalLastsAsync(string ticker, DateTime time, int count)
        {
            _token?.ThrowIfCancellationRequested();

            CancellationTokenSource source = new CancellationTokenSource(Debugger.IsAttached ? -1 : RequestTimeoutInMs);

            int reqId = -1;
            var tmpList = new LinkedList<Last>();

            var tcs = new TaskCompletionSource<IEnumerable<Last>>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.Token.Register(() => tcs.TrySetException(new TimeoutException($"GetHistoricalLastsAsync")));
            var historicalTicks = new Action<int, IEnumerable<IBApi­.HistoricalTickLast>, bool>((rId, data, isDone) =>
            {
                if (_token != null && _token.HasValue && _token.Value.IsCancellationRequested)
                    tcs.TrySetCanceled();

                if (rId == reqId)
                {
                    foreach (var d in data)
                    {
                        if (_token != null && _token.HasValue && _token.Value.IsCancellationRequested)
                            tcs.TrySetCanceled();

                        tmpList.AddLast((Last)d);
                    }

                    if (isDone)
                        tcs.TrySetResult(tmpList);
                }
            });
            var error = new Action<ErrorMessage>(msg => tcs.TrySetException(msg));

            _logger?.Trace($"Retrieving {count} 'Lasts' from TWS for '{ticker}' descending from {time}.");
            var contract = _client.ContractsCache.Get(ticker);
            TickRequest req = new(contract, string.Empty, $"{time.ToString("yyyyMMdd HH:mm:ss")} US/Eastern", count, "TRADES");
            _pvc.CheckRequest(req);

            _client.Responses.HistoricalTicksLast += historicalTicks;
            _client.Responses.Error += error;
            try
            {
                reqId = _client.RequestHistoricalTicks(contract, string.Empty, $"{time.ToString("yyyyMMdd HH:mm:ss")} US/Eastern", count, "TRADES", false, true);

                _pvc.NbRequest++;
                await tcs.Task;

                if (tcs.Task.IsFaulted || tcs.Task.IsCanceled)
                    _client.CancelHistoricalData(reqId);
                _token?.ThrowIfCancellationRequested();
            }
            finally
            {
                _client.Responses.HistoricalTicksLast -= historicalTicks;
                _client.Responses.Error -= error;
            }

            return tcs.Task.Result;
        }

        static void ValidateDates(DateTime from, DateTime to)
        {
            if (from >= to)
                throw new ArgumentException("Starting date is after or equal to ending date");

            // https://interactivebrokers.github.io/tws-api/historical_limitations.html
            if (DateTime.Now - from > TimeSpan.FromDays(6 * 30))
                throw new ArgumentException($"Bars whose size is 30 seconds or less older than six months are not available. {from}");

            IEnumerable<(DateTime, DateTime)> days = MarketDataUtils.GetMarketDays(from, to).ToList();
            if (!days.Any())
                throw new ArgumentException($"Market was closed from {from} to {to}");
        }

        // TWS API limitations. Pacing violation occurs when : 
        // - Making identical historical data requests within 15 seconds.
        // - Making six or more historical data requests for the same Contract, Exchange and Tick Type within two seconds.
        // - Making more than 60 requests within any 10 minute period.
        // https://interactivebrokers.github.io/tws-api/historical_limitations.html
        class PacingViolationChecker
        {
            const string RequestFileName = "pvc";

            ILogger? _logger;
            int _nbRequest = 0;

            Dictionary<BarRequest, DateTime> _barRequests = new ();
            Dictionary<TickRequest, DateTime> _tickRequests = new();
            List<DateTime> _requestTimes;
            CancellationToken? _token;

            public PacingViolationChecker(ILogger? logger)
            {
                _logger = logger;
                ReadRequestFile();
            }

            internal ILogger? Logger { get => _logger; set => _logger = value; }
            internal CancellationToken? Token { get => _token; set => _token = value; }


            [MemberNotNull(nameof(_requestTimes))]
            void ReadRequestFile()
            {
                if (!File.Exists(RequestFileName))
                {
                    var stream = File.CreateText(RequestFileName);
                    stream.Close();
                }

                _requestTimes = new(File.ReadAllLines(RequestFileName).Select(l => DateTime.Parse(l)));
                _nbRequest = _requestTimes.Count(rt => DateTime.Now - rt < TimeSpan.FromMinutes(10));
            }

            void WriteRequestFile()
            {
                File.WriteAllText(
                    RequestFileName, 
                    string.Join(Environment.NewLine, _requestTimes
                        .Where(rt => DateTime.Now - rt < TimeSpan.FromMinutes(10))
                        .Select(rt => rt.ToString())
                        )
                    );
            }

            internal int NbRequest
            {
                get => _nbRequest;
                set
                {
                    _nbRequest = value;
                    _logger?.Trace($"Current nb of requests : {_nbRequest}.");
                    _requestTimes.Add(DateTime.Now);
                    WriteRequestFile();

                    CheckForNbRequestsPacingViolations();
                }
            }

            internal void CheckRequest(BarRequest req)
            {
                CheckRequestInternal(req, _barRequests);
            }

            internal void CheckRequest(TickRequest req)
            {
                CheckRequestInternal(req, _tickRequests);
            }

            void CheckRequestInternal<TRecord>(TRecord req, IDictionary<TRecord, DateTime> dict)
            {
                if (dict.ContainsKey(req))
                {
                    var elapsed = DateTime.Now - dict[req];
                    TimeSpan _15sec = TimeSpan.FromSeconds(15);
                    if (elapsed < _15sec)
                    {
                        TimeSpan toWait = _15sec - elapsed + TimeSpan.FromMilliseconds(250);
                        _logger?.Debug($"Same request made within 15 seconds. Waiting {Math.Round(toWait.TotalSeconds, 1)} seconds...");

                        Task.Delay(toWait).Wait(_token ?? CancellationToken.None);
                    }
                }

                dict[req] = DateTime.Now;
            }

            void CheckForNbRequestsPacingViolations()
            {
                if (_nbRequest != 0 && _nbRequest % 60 == 0)
                {
                    int minutes = 10;
                    _logger?.Debug($"60 requests made : waiting {minutes} minutes...");
                    WaitFor(minutes);
                    _logger?.Debug($"Resuming.");
                }
                else if (_nbRequest != 0 && _nbRequest % 5 == 0)
                {
                    _logger?.Debug($"5 requests made : waiting 2 seconds...");
                    Task.Delay(2000).Wait(_token ?? CancellationToken.None);
                    _logger?.Debug($"Resuming.");
                }
            }

            void WaitFor(int minutes)
            {
                for (int i = 0; i < minutes; ++i)
                {
                    Task.Delay(60 * 1000).Wait(_token ?? CancellationToken.None);
                    if (i < minutes - 1)
                        _logger?.Debug($"{9 - i} minutes left...");
                }
            }
        }
    }
}
