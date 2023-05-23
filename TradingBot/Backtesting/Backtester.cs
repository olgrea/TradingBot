using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using NLog;
using TradingBot.Broker;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.MarketData.Providers;
using TradingBot.Broker.Orders;
using TradingBot.IBKR;
using TradingBot.IBKR.Client;
using TradingBot.Utils;

namespace TradingBot.Backtesting
{
    public class TimeCompression
    {
        public const double DefaultCompressionFactor = 0.001;
        public double Factor = DefaultCompressionFactor;

        // ticks param : expressed in 100-nanosecond units.
        public TimeSpan OneSecond => new TimeSpan(ticks: (long)Math.Round(1 * 1_000_000 * Factor * 10));
    }

    public struct BacktestingResults
    {
        public DateTime Start {get; set;}
        public DateTime End {get; set;}
        public TimeSpan RunTime { get; set; } 
    }

    public class Backtester : IBroker, IAsyncDisposable
    {
        private const string FakeAccountCode = "FAKEACCOUNT123";
        Account _fakeAccount = new Account(FakeAccountCode)
        {
            Code = FakeAccountCode,
            CashBalances = new Dictionary<string, double>()
            {
                { "USD", 25000.00 },
            },
            UnrealizedPnL = new Dictionary<string, double>()
            {
                { "USD", 0.00 },
            },
            RealizedPnL = new Dictionary<string, double>()
            {
                { "USD", 0.00 },
            }
        };

        bool _accountUpdatesRequested = false;
        DateTime? _lastAccountUpdateTime;

        double _totalCommission = 0.0;

        IBBroker _broker;
        ConcurrentQueue<Action> _requestsQueue = new ConcurrentQueue<Action>();

        Task? _consumerTask;
        ManualResetEventSlim _advanceTimeEvent = new(true);
        ConcurrentDictionary<string, Task> _marketDataBackgroundTasks = new();
        CancellationTokenSource? _cancellation;
        TaskCompletionSource<BacktestingResults>? _startTcs = null;
        bool _isRunning = false;

        TimeCompression _timeCompression = new();
        Stopwatch _totalRuntimeStopwatch = new Stopwatch();
        Stopwatch _µsWaitStopwatch = new Stopwatch();
        
        DateTime _start;
        DateTime _end;
        DateTime _currentTime;
        DateTime? _lastProcessedTime;
        Dictionary<(string, string), DateTime> _timeSlicesUpperBounds = new();
        
        BacktesterProgress _progress = new BacktesterProgress();
        BacktestingResults _result = new BacktestingResults();
        ILogger? _logger;

        public Backtester(DateOnly date, ILogger? logger = null) : this(date.ToDateTime(default).ToMarketHours().Item1, date.ToDateTime(default).ToMarketHours().Item2, logger) { }

        public Backtester(DateTime from, DateTime to, ILogger? logger = null)
        {
            if(to - from > TimeSpan.FromDays(1))
                throw new ArgumentException("Can only backtest a single day.");

            if (from >= DateTime.Now || to >= DateTime.Now)
                throw new ArgumentException("Can't backtest the future!");

            logger ??= LogManager.GetLogger($"Backtester");

            _fakeAccount.Time = _currentTime = _result.Start = _start = from;
            _end = _result.End = to;
            _logger = logger;

            _broker = new IBBroker(191919, logger);
            LiveDataProvider = new BacktesterLiveDataProvider(this);
            HistoricalDataProvider = _broker.HistoricalDataProvider;
            OrderManager = new BacktesterOrderManager(this);

            ClockTick += OnClockTick_SendAccountUpdates;
            ClockTick += OnClockTick_UpdateUnrealizedPnL;
        }

        public async ValueTask DisposeAsync()
        {
            Stop();
            await DisconnectAsync();
            _cancellation?.Dispose();
            _consumerTask?.Dispose();
            foreach(Task t in _marketDataBackgroundTasks.Values)
                t.Dispose();
            _startTcs?.TrySetException(new ObjectDisposedException(nameof(Backtester)));
        }

        public TimeCompression TimeCompression { get => _timeCompression; internal set => _timeCompression = value; }
        internal DateTime StartTime => _start;
        internal DateTime EndTime => _end;
        internal DateTime CurrentTime => _currentTime;
        internal DateTime? LastProcessedTime => _lastProcessedTime;
        internal ILogger? Logger { get => _logger; set => _logger = value; }
        internal Account Account => _fakeAccount;
        internal event Action<DateTime, CancellationToken>? ClockTick;

        internal string? DbPath
        {
            get => (HistoricalDataProvider as IBHistoricalDataProvider)?.DbPath;
            set
            {
                if(HistoricalDataProvider is IBHistoricalDataProvider ibh)
                    ibh.DbPath = value!;
            }
        }

        public ILiveDataProvider LiveDataProvider { get; private set; }
        public IHistoricalDataProvider HistoricalDataProvider { get; init; }
        public IOrderManager OrderManager { get; init; }
        public event Action<Exception>? ErrorOccured;
        public event Action<Message>? MessageReceived;
        public event Action<AccountValue>? AccountValueUpdated;
        public event Action<Position>? PositionUpdated;
        public event Action<PnL>? PnLUpdated;
        public event Action<BacktesterProgress>? ProgressHandler;

        public async Task<string> ConnectAsync()
        {
            return await ConnectAsync(CancellationToken.None);
        }

        public Task<string> ConnectAsync(CancellationToken token)
        {
            if (_consumerTask != null)
                throw new ErrorMessageException("Already connected");

            _logger?.Debug($"Backtester connected.");
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            _cancellation.Token.Register(() => _startTcs?.TrySetCanceled());
            _consumerTask = Task.Factory.StartNew(() => ConsumeRequests(_cancellation.Token), _cancellation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current);
            _consumerTask.ContinueWith(HandleTaskError, TaskContinuationOptions.OnlyOnFaulted);

            return Task.FromResult(FakeAccountCode);
        }

        internal void HandleTaskError(Task t)
        {
            var e = t.Exception ?? new Exception("Unknown exception occured in Backtester");
            _startTcs?.TrySetException(e);
            ErrorOccured?.Invoke(e);
            // cancel other tasks on error?
        }

        public async Task DisconnectAsync()
        {
            if (_consumerTask == null || _cancellation == null || _cancellation.IsCancellationRequested)
                return;

            await _broker.DisconnectAsync();
            _cancellation.Cancel();

            var list = new List<Task>();
            try
            {
                if (_consumerTask != null)
                    list.Add(_consumerTask);

                if (_marketDataBackgroundTasks.Any())
                    list.AddRange(_marketDataBackgroundTasks.Values);

                await Task.WhenAll(list);
            }
            catch (AggregateException ae)
            {
                ae.Flatten().Handle(ex => ex is TaskCanceledException);
            }
            finally
            {
                _cancellation?.Dispose();
                _cancellation = null;
                _consumerTask?.Dispose();
                _consumerTask = null;
                foreach (Task t in _marketDataBackgroundTasks.Values)
                    t.Dispose();
                _marketDataBackgroundTasks.Clear();

                _logger?.Debug($"Backtester disconnected.");
            }
        }

        public Task<BacktestingResults> Start()
        {
            if(_consumerTask == null)
                throw new InvalidOperationException($"Not connected");
            else if (IsDayOver())
            {
                _logger?.Debug($"Backtesting of {_start.Date} already completed.");
                return Task.FromResult(_result);
            }

            if (_startTcs == null)
                _logger?.Debug($"Backtester started (from {_start} to {_end})");
            else
                _logger?.Debug($"Backtester resumed at {_currentTime}");

            _logger?.Debug($"Current db : {DbPath}");
            _startTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            _totalRuntimeStopwatch.Start();
            _isRunning = true;
            return _startTcs.Task;
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            // We wait until the latest AdvanceTime() iteration is complete
            _advanceTimeEvent.Wait();

            _totalRuntimeStopwatch.Stop();
            _logger?.Debug($"Backtester stopped at {_currentTime}");
        }

        public void Reset()
        {
            Stop();

            _requestsQueue.Clear();
            _currentTime = _start;
            _lastProcessedTime = null;
            _result.RunTime = TimeSpan.Zero;
            _totalRuntimeStopwatch.Reset();
            _startTcs = null;

            // TODO : handle this better
            LiveDataProvider.Dispose();
            LiveDataProvider = new BacktesterLiveDataProvider(this);

            _logger?.Trace($"Backtester reset");
        }

        internal void EnqueueRequest(Action action)
        {
            if (_consumerTask == null)
                throw new InvalidOperationException($"Consumer task not started.");

            if (_consumerTask.IsFaulted || _consumerTask.IsCanceled)
                _consumerTask.Wait();

            _logger?.Trace($"EnqueueRequest : {action.GetMethodInfo().Name}");
            _requestsQueue.Enqueue(action);
        }

        void ConsumeRequests(CancellationToken token)
        {
            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    // Let's process the requests first 
                    while (_requestsQueue.TryDequeue(out Action? action))
                    {
                        token.ThrowIfCancellationRequested();
                        action();
                        _logger?.Trace($"Request consumed : {action.GetMethodInfo().Name}");
                    }

                    if (!_isRunning)
                        continue;

                    if(IsDayOver())
                    {
                        Stop();
                        _result.RunTime = _totalRuntimeStopwatch.Elapsed;
                        _logger?.Debug($"Backtesting finished. Runtime : {_result.RunTime}");
                        _startTcs?.TrySetResult(_result);
                    }
                    else 
                        AdvanceTime(token);
                }
            }
            catch (OperationCanceledException)
            {
                _logger?.Debug("Consumer task cancelled");
            }
        }

        void AdvanceTime(CancellationToken token)
        {
            try
            {
                if (!_isRunning)
                    return;

                _advanceTimeEvent.Reset();

                _logger?.Debug($"Processing time {_currentTime}");

                token.ThrowIfCancellationRequested();
                ClockTick?.Invoke(_currentTime, token);
                token.ThrowIfCancellationRequested();

                WaitOneSecond(token);

                token.ThrowIfCancellationRequested();

                _progress.Time = _currentTime;
                ProgressHandler?.Invoke(_progress);

                token.ThrowIfCancellationRequested();
                _lastProcessedTime = _currentTime;
                _currentTime = _currentTime.AddSeconds(1);
            }
            finally
            {
                _advanceTimeEvent.Set();
            }
        }

        void WaitOneSecond(CancellationToken mainToken)
        {
            var timeToWait = TimeCompression.OneSecond;
            if (timeToWait.TotalMilliseconds > 20)
            {
                // resolution of Delay is around 15 milliseconds on Windows
                Task.Delay(timeToWait).Wait(mainToken);
                return;
            }

            // µs resolution. Not always super accurate but it's good enough
            long nbMicroSecToWait = timeToWait.Ticks / 10;
            double microSecPerTick = 1_000_000.0 / Stopwatch.Frequency;

            _µsWaitStopwatch.Restart();
            Thread.SpinWait(10);
            while((long)(_µsWaitStopwatch.ElapsedTicks * microSecPerTick) < nbMicroSecToWait)
                Thread.SpinWait(10);
            _µsWaitStopwatch.Stop();
            
            _logger?.Trace($"expected : {nbMicroSecToWait} µs, actual : {_µsWaitStopwatch.ElapsedTicks*microSecPerTick} µs");
        }

        bool IsDayOver() => _currentTime >= _end;

        internal async Task<IEnumerable<TData>> GetAsync<TData>(string ticker, DateTime from, DateTime to) where TData : IMarketData, new()
        {
            return await GetAsync<TData>(ticker, from, to, _cancellation!.Token);
        }

        internal async Task<IEnumerable<TData>> GetAsync<TData>(string ticker, DateTime from, DateTime to, CancellationToken token) where TData : IMarketData, new()
        {
            if (from > to)
                throw new ArgumentException($"'from' is greater than 'to'");
            
            var data = (await _broker.HistoricalDataProvider.GetHistoricalDataAsync<TData>(ticker, from, to, token))
                .OrderBy(d => d.Time)
                .Cast<TData>();

            // The rest on a background task
            GetDailyDataOnBackgroundTask<TData>(ticker, token);

            return data;
        }

        internal async Task<IEnumerable<TData>> GetAsync<TData>(string ticker, DateTime dateTime) where TData : IMarketData, new()
        {
            return await GetAsync<TData>(ticker, dateTime, _cancellation!.Token);
        }

        internal async Task<IEnumerable<TData>> GetAsync<TData>(string ticker, DateTime dateTime, CancellationToken token) where TData : IMarketData, new()
        {
            // Retrieving data in slices of 10 mins.
            var lower = dateTime;
            var upper = dateTime.AddSeconds(1);

            var key = (ticker, typeof(TData).Name);
            if (!_timeSlicesUpperBounds.ContainsKey(key) || _timeSlicesUpperBounds[key] <= dateTime)
            {
                upper = dateTime.Ceiling(TimeSpan.FromMinutes(10));
                if (upper >= EndTime)
                    upper = EndTime;

                lower = dateTime.Floor(TimeSpan.FromMinutes(10));
                if (lower < StartTime)
                    lower = StartTime;

                Debug.Assert(upper - dateTime >= TimeSpan.FromSeconds(1));
                _timeSlicesUpperBounds[key] = upper;
            }

            var data = (await _broker.HistoricalDataProvider.GetHistoricalDataAsync<TData>(ticker, lower, upper, token))
                .Where(d => d.Time == dateTime)
                .Cast<TData>();

            // The rest on a background task
            GetDailyDataOnBackgroundTask<TData>(ticker, token);

            return data;
        }

        private void GetDailyDataOnBackgroundTask<TData>(string ticker, CancellationToken token) where TData : IMarketData, new()
        {
            _ = _marketDataBackgroundTasks.GetOrAdd(ticker, t =>
            {
                _logger?.Debug($"Market data background task started for {ticker}");
                var task = Task.Run(async () =>
                {
                    try
                    {
                        await _broker.HistoricalDataProvider.GetHistoricalDataAsync<TData>(ticker, StartTime, EndTime, token);
                    }
                    catch (OperationCanceledException)
                    {
                        Logger?.Debug($"Market data background task for {ticker} cancelled");
                    }
                }, token);

                _ = task.ContinueWith(HandleTaskError, TaskContinuationOptions.OnlyOnFaulted);

                return task;
            });
        }

        public Task<Account> GetAccountAsync()
        {
            _lastAccountUpdateTime = _currentTime;
            _accountUpdatesRequested = true;
            SendAccountUpdates();
            return Task.FromResult(_fakeAccount);
        }

        public void RequestAccountUpdates()
        {
            if (_accountUpdatesRequested)
                return;

            EnqueueRequest(() =>
            {
                _accountUpdatesRequested = true;
                SendAccountUpdates();
            });
        }

        void OnClockTick_SendAccountUpdates(DateTime newTime, CancellationToken token)
        {
            if (!_accountUpdatesRequested || token!.IsCancellationRequested) 
                return;

            if (_lastAccountUpdateTime == null || _currentTime - _lastAccountUpdateTime > TimeSpan.FromMinutes(3))
            {
                SendAccountUpdates();
            }
        }

        internal void SendAccountUpdates()
        {
            AccountValueUpdated?.Invoke(new AccountValue(AccountValueKey.CashBalance, Account.CashBalances["USD"].ToString(), "USD"));
            AccountValueUpdated?.Invoke(new AccountValue(AccountValueKey.RealizedPnL, Account.RealizedPnL["USD"].ToString(), "USD"));
            AccountValueUpdated?.Invoke(new AccountValue(AccountValueKey.UnrealizedPnL, Account.UnrealizedPnL["USD"].ToString(), "USD"));
            AccountValueUpdated?.Invoke(new AccountValue(AccountValueKey.Time, _currentTime.ToString()));

            _logger?.Trace($"Sending account update : time : {_currentTime}");
            Account.Time = _currentTime;
            _lastAccountUpdateTime = _currentTime;
        }

        public void CancelAccountUpdates()
        {
            EnqueueRequest(() => _accountUpdatesRequested = false);
        }

        internal double UpdateCommissions(Order order, double price)
        {
            double commission = GetCommission(order, price);
            Logger?.Debug($"{order} : commission : {commission:c}");

            UpdateCashBalance(-commission);
            _totalCommission += commission;
            return commission;
        }

        internal void UpdateCashBalance(double total)
        {
            _fakeAccount.CashBalances["USD"] += total;
        }

        internal void UpdateRealizedPNL(string ticker, double totalQty, double price)
        {
            Position position = Account.Positions[ticker];
            var realized = totalQty * (price - position.AverageCost);

            position.RealizedPNL += realized;
            _fakeAccount.RealizedPnL["USD"] += realized;

            Logger?.Debug($"Account {_fakeAccount.Code} :  Realized PnL  : {position.RealizedPNL:c}");
        }

        internal void UpdateUnrealizedPNL(string ticker, double currentPrice)
        {
            Position position = Account.Positions[ticker];

            position.Price = currentPrice;
            position.TotalMarketValue = currentPrice * position.PositionAmount;

            var positionValue = position.PositionAmount * position.AverageCost;
            var unrealizedPnL = position.TotalMarketValue - positionValue;

            position.UnrealizedPNL = unrealizedPnL;
            _fakeAccount.UnrealizedPnL["USD"] = unrealizedPnL;

            //_logger.Debug($"Account {_fakeAccount.Code} :  Unrealized PnL  : {Position.UnrealizedPNL:c}  (position value : {positionValue:c} market value : {Position.MarketValue:c})");
        }

        double GetCommission(Order order, double price)
        {
            //https://www.interactivebrokers.ca/en/index.php?f=1590

            // fixed rates
            double @fixed = 0.005;
            double min = 1.0;
            double max = order.TotalQuantity * price * 0.01; // 1% of trade value

            return Math.Min(Math.Max(@fixed * order.TotalQuantity, min), max);
        }

        internal void OnPositionUpdated(Position pos)
        {
            _lastAccountUpdateTime = _currentTime;
            PositionUpdated?.Invoke(pos);
        }

        //TODO : to confirm : what does "real time" means here? See link
        // https://interactivebrokers.github.io/tws-api/pnl.html
        void OnClockTick_UpdateUnrealizedPnL(DateTime newTime, CancellationToken token)
        {
            foreach (KeyValuePair<string, Position> pos in Account.Positions.Where(p => p.Value.PositionAmount > 0))
            {
                token.ThrowIfCancellationRequested();

                var lasts = GetAsync<Last>(pos.Key, newTime, token).Result;
                if(lasts.Any())
                {
                    // TODO : why the average?
                    var lastPriceAvg = lasts.Average(l => l.Price);

                    UpdateUnrealizedPNL(pos.Key, lastPriceAvg);
                    PnLUpdated?.Invoke(pos.Value.ToPnL());
                }
            }
        }

        public Task<DateTime> GetServerTimeAsync()
        {
            return Task.FromResult(_currentTime);
        }
    }
}
