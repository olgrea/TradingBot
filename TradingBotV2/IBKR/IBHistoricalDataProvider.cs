using System.Diagnostics;
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

        // for unit tests
        internal int _nbRetrievedFromDb = 0;
        internal int _nbRetrievedFromIBKR = 0;
        internal int _nbInsertedInDb = 0;

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
        ILogger _logger;
        string _dbPath;

        public IBHistoricalDataProvider(IBClient client, string dbPath = null) : this(client, null, dbPath) {}

        public IBHistoricalDataProvider(IBClient client, ILogger logger, string dbPath = null)
        {
            _client = client;
            _logger = logger;
            _dbPath = dbPath ?? DefaultDbPath;
        }

        public string DbPath { get => _dbPath; internal set => _dbPath = value; }
        public bool EnableDb { get; set; } = true;

        public async Task<IEnumerable<Bar>> GetHistoricalOneSecBarsAsync(string ticker, DateTime date)
        {
            return await GetHistoricalData(ticker, date, new BarCommandFactory(BarLength._1Sec, _dbPath));
        }

        public async Task<IEnumerable<Bar>> GetHistoricalOneSecBarsAsync(string ticker, DateTime from, DateTime to)
        {
            return await GetHistoricalData(ticker, from, to, new BarCommandFactory(BarLength._1Sec, _dbPath));
        }

        public async Task<IEnumerable<BidAsk>> GetHistoricalBidAsksAsync(string ticker, DateTime date)
        {
            return await GetHistoricalData(ticker, date, new BidAskCommandFactory(_dbPath));
        }

        public async Task<IEnumerable<BidAsk>> GetHistoricalBidAsksAsync(string ticker, DateTime from, DateTime to)
        {
            return await GetHistoricalData(ticker, from, to, new BidAskCommandFactory(_dbPath));
        }

        public async Task<IEnumerable<Last>> GetHistoricalLastsAsync(string ticker, DateTime date)
        {
            return await GetHistoricalData(ticker, date, new LastCommandFactory(_dbPath));
        }

        public async Task<IEnumerable<Last>> GetHistoricalLastsAsync(string ticker, DateTime from, DateTime to)
        {
            return await GetHistoricalData(ticker, from, to, new LastCommandFactory(_dbPath));
        }

        private async Task<IEnumerable<TData>> GetHistoricalData<TData>(string ticker, DateTime date, DbCommandFactory<TData> commandFactory) where TData : IMarketData, new()
        {
            // TODO : use new DateOnly struct?
            date = new DateTime(date.Date.Ticks + MarketDataUtils.MarketStartTime.Ticks);

            ValidateDate(date);

            if (!MarketDataUtils.WasMarketOpen(date))
                throw new ArgumentException($"The market was closed on {date}");

            return await GetDataForDay(date, MarketDataUtils.MarketDayTimeRange, ticker, commandFactory);
        }

        private async Task<IEnumerable<TData>> GetHistoricalData<TData>(string ticker, DateTime from, DateTime to, DbCommandFactory<TData> commandFactory) where TData : IMarketData, new()
        {
            ValidateDate(from);

            IEnumerable<(DateTime, DateTime)> days = MarketDataUtils.GetMarketDays(from, to).ToList();
            if(!days.Any())
                throw new ArgumentException($"Market was closed from {from} to {to}");

            IEnumerable<TData> data = new LinkedList<TData>();
            foreach ((DateTime, DateTime) day in days)
            {
                data = data.Concat(await GetDataForDay(day.Item1.Date, (day.Item1.TimeOfDay, day.Item2.TimeOfDay), ticker, commandFactory));
            }
            return data;
        }

        async Task<IEnumerable<TData>> GetDataForDay<TData>(DateTime date, (TimeSpan, TimeSpan) timeRange, string ticker, DbCommandFactory<TData> commandFactory) where TData : IMarketData, new()
        {
            Type datatype = typeof(TData);
            DateTime morning = new DateTime(date.Date.Ticks + timeRange.Item1.Ticks);
            DateTime current = new DateTime(date.Date.Ticks + timeRange.Item2.Ticks);

            _logger?.Info($"Getting {datatype.Name} for {ticker} on {date.ToShortDateString()} ({morning.ToShortTimeString()} to {current.ToShortTimeString()})");

            // In order to respect TWS limitations, data is retrieved in chunks of 30 minutes for bars of 1 sec length (1800 bars total), from the end of the
            // time range to the beginning. 
            // https://interactivebrokers.github.io/tws-api/historical_limitations.html

            _nbRetrievedFromDb = 0;
            _nbRetrievedFromIBKR = 0;
            _nbInsertedInDb = 0;
            IEnumerable<TData> dailyData = Enumerable.Empty<TData>();
            while (current > morning)
            {
                var begin = current.AddMinutes(-30);
                var end = current;

                bool exists = false;
                if (EnableDb)
                {
                    DbCommand<bool> existsCmd = commandFactory.CreateExistsCommand(ticker, current.Date, (begin.TimeOfDay, end.TimeOfDay));
                    exists = existsCmd.Execute();
                }

                IEnumerable<TData> data;
                if (exists)
                {
                    // If the data exists in the database, retrieve it
                    var selectCmd = commandFactory.CreateSelectCommand(ticker, current.Date, (begin.TimeOfDay, end.TimeOfDay));
                    data = selectCmd.Execute();
                    _nbRetrievedFromDb += data.Count();

                    var dateStr = current.Date.ToShortDateString();
                    _logger?.Info($"{datatype.Name} for {ticker} {dateStr} ({begin.ToShortTimeString()}-{end.ToShortTimeString()}) already exists in db. Skipping.");
                }
                else
                {
                    // Otherwise get it from the server and insert it in the db
                    data = await FetchHistoricalDataFromServer<TData>(ticker, current);
                    _nbRetrievedFromIBKR += data.Count();

                    var dateStr = current.Date.ToShortDateString();
                    _logger?.Info($"{datatype.Name} for {ticker} {dateStr} ({begin.ToShortTimeString()}-{end.ToShortTimeString()}) received from TWS.");

                    if(EnableDb)
                    {
                        _logger?.Info($"Inserting`in db.");
                        DbCommand<bool> insertCmd = commandFactory.CreateInsertCommand(ticker, data);
                        if(insertCmd.Execute() && insertCmd is InsertCommand<TData> iCmd)
                            _nbInsertedInDb += iCmd.NbInserted;
                    }
                }

                dailyData = data.Concat(dailyData);
                current = current.AddMinutes(-30);
            }

            return dailyData;
        }

        async Task<IEnumerable<TData>> FetchHistoricalDataFromServer<TData>(string ticker, DateTime time) where TData : IMarketData, new()
        {
            if (typeof(TData) == typeof(Bar))
            {
                return await FetchBars<TData>(ticker, time);
            }
            else
            {
                return await FetchTooMuchData<TData>(ticker, time);
            }
        }

        private async Task<IEnumerable<TData>> FetchTooMuchData<TData>(string ticker, DateTime time) where TData : IMarketData, new()
        {
            Debug.Assert(MarketDataUtils.WasMarketOpen(time));

            Type datatype = typeof(TData);
            _logger?.Info($"Retrieving {datatype.Name} from TWS for '{ticker} {time}'.");

            // Max nb of ticks per request is 1000, but since there can be multiple BidAsk per second it's not possible to know how many
            // ticks are needed for 30 minutes.
            // So we just do requests as long as we don't have 30 minutes.
            // Inefficient because we're retrieving more data than we need but it works...
            IEnumerable<TData> data = new LinkedList<TData>();
            DateTime current = time;
            TimeSpan _30min = TimeSpan.FromMinutes(30);
            var diff = time - current;
            int tickCount = 1000;
            while (diff <= _30min)
            {
                IEnumerable<TData> ticks = Enumerable.Empty<TData>();
                if (datatype == typeof(BidAsk))
                {
                    ticks = (await GetHistoricalBidAsksAsync(ticker, current, tickCount)).Cast<TData>();
                    // Note that when BID_ASK historical data is requested, each request is counted twice according to the doc
                    NbRequest++; NbRequest++;
                }
                else if (datatype == typeof(Last))
                {
                    ticks = (await GetHistoricalLastsAsync(ticker, current, tickCount)).Cast<TData>();
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

        private async Task<IEnumerable<TData>> FetchBars<TData>(string ticker, DateTime time) where TData : IMarketData, new()
        {
            Debug.Assert(MarketDataUtils.WasMarketOpen(time));
            _logger?.Info($"Retrieving bars from TWS for '{ticker} {time}'.");
            var bars = await GetHistoricalBarsAsync(ticker, BarLength._1Sec, time, 1800);
            NbRequest++;
            return bars.Cast<TData>();
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

            string durationStr = null;
            string barSizeStr = null;
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

            _client.Responses.HistoricalTicksBidAsk += historicalTicks;
            try
            {
                var contract = _client.ContractsCache.Get(ticker);
                reqId = _client.RequestHistoricalTicks(contract, null, $"{time.ToString("yyyyMMdd HH:mm:ss")} US/Eastern", count, "BID_ASK", false, true);
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.HistoricalTicksBidAsk -= historicalTicks;
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

            _client.Responses.HistoricalTicksLast += historicalTicks;
            try
            {
                var contract = _client.ContractsCache.Get(ticker);
                reqId = _client.RequestHistoricalTicks(contract, null, $"{time.ToString("yyyyMMdd HH:mm:ss")} US/Eastern", count, "TRADES", false, true);
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.HistoricalTicksLast -= historicalTicks;
            }
        }
        
        private void CheckForPacingViolations()
        {
            // TWS API limitations. Pacing violation occurs when : 
            // - Making identical historical data requests within 15 seconds.
            // - Making six or more historical data requests for the same Contract, Exchange and Tick Type within two seconds.
            // - Making more than 60 requests within any ten minute period.
            // https://interactivebrokers.github.io/tws-api/historical_limitations.html

            if (_nbRequest == 60)
            {
                _logger?.Info($"60 requests made : waiting 10 minutes...");
                Wait10Minutes();
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

        void Wait10Minutes()
        {
            for (int i = 0; i < 10; ++i)
            {
                Task.Delay(60 * 1000).Wait();
                if (i < 9)
                    _logger?.Info($"{9 - i} minutes left...");
            }
        }

        private static void ValidateDate(DateTime date)
        {
            // https://interactivebrokers.github.io/tws-api/historical_limitations.html
            if (DateTime.Now - date > TimeSpan.FromDays(6 * 30))
                throw new ArgumentException($"Bars whose size is 30 seconds or less older than six months are not available. {date}");
        }
    }
}
