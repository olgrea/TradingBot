using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using NLog;
using TradingBotV2.Broker;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.MarketData.Providers;
using TradingBotV2.Broker.Orders;
using TradingBotV2.IBKR;
using TradingBotV2.IBKR.Client;

namespace TradingBotV2.Backtesting
{
    internal class TimeCompression
    {
        public const double DefaultCompressionFactor = 0.001;
        public double Factor = DefaultCompressionFactor;

        // ticks param : expressed in 100-nanosecond units.
        public TimeSpan OneSecond => new TimeSpan(ticks: (long)Math.Round(1 * 1_000_000 * Factor * 10));
    }

    internal struct BacktestingResults
    {
        public DateTime Start {get; set;}
        public DateTime End {get; set;}
        public TimeSpan RunTime { get; set; } 
    }

    internal class Backtester : IBroker, IAsyncDisposable
    {
        private const string FakeAccountCode = "FAKEACCOUNT123";
        Account _fakeAccount = new Account(FakeAccountCode)
        {
            Code = FakeAccountCode,
            CashBalances = new Dictionary<string, double>()
                {
                    { "BASE", 25000.00 },
                    { "USD", 25000.00 },
                },
            UnrealizedPnL = new Dictionary<string, double>()
                {
                    { "BASE", 0.00},
                    { "USD", 0.00 },
                },
            RealizedPnL = new Dictionary<string, double>()
                {
                    { "BASE", 0.00 },
                    { "USD", 0.00 },
                }
        };

        double _totalCommission = 0.0;

        IBBroker _broker;
        ConcurrentQueue<Action> _requestsQueue = new ConcurrentQueue<Action>();

        Task? _consumerTask;
        Task? _marketDataBackgroundTask;
        CancellationTokenSource? _cancellation;
        TaskCompletionSource<BacktestingResults>? _startTcs = null;
        bool _isRunning = false;

        internal TimeCompression TimeCompression = new();
        Stopwatch _totalRuntimeStopwatch = new Stopwatch();
        Stopwatch _µsWaitStopwatch = new Stopwatch();
        
        DateTime _start;
        DateTime _end;
        DateTime _currentTime;
        DateTime? _lastProcessedTime;
        Dictionary<(string, Type), DateTime> _timeSlicesUpperBounds = new();
        
        BacktesterProgress _progress = new BacktesterProgress();
        BacktestingResults _result = new BacktestingResults();
        ILogger? _logger;

        public Backtester(DateOnly date, ILogger? logger = null) : this(date.ToDateTime(default).ToMarketHours().Item1, date.ToDateTime(default).ToMarketHours().Item2, logger) { }

        public Backtester(DateTime from, DateTime to, ILogger? logger = null)
        {
            if(to - from > TimeSpan.FromDays(1))
                throw new ArgumentException("Can only backtest a single day.");

            if (from.Date == DateTime.Now.Date || to.Date == DateTime.Now.Date)
                throw new ArgumentException("Can't backtest the current day.");

            _fakeAccount.Time = _currentTime = _result.Start = _start = from;
            _end = _result.End = to;
            _logger = logger;

            _broker = new IBBroker(191919, logger);
            LiveDataProvider = new BacktesterLiveDataProvider(this);
            HistoricalDataProvider = new IBHistoricalDataProvider(_broker, logger);
            OrderManager = new BacktesterOrderManager(this);
        }

        public async ValueTask DisposeAsync()
        {
            Stop();
            await DisconnectAsync();
            _cancellation?.Dispose();
            _consumerTask?.Dispose();
            _marketDataBackgroundTask?.Dispose();
        }

        internal DateTime StartTime => _start;
        internal DateTime EndTime => _end;
        internal DateTime CurrentTime => _currentTime;
        internal DateTime? LastProcessedTime => _lastProcessedTime;
        internal ILogger? Logger { get => _logger; set => _logger = value; }
        internal Account Account => _fakeAccount;
        internal event Action<DateTime>? ClockTick;

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
        
        public event Action<AccountValue>? AccountValueUpdated;
        public event Action<BacktesterProgress>? ProgressHandler;

        public Task<string> ConnectAsync()
        {
            if (_consumerTask != null)
                throw new ErrorMessage("Already connected");

            _logger?.Trace($"Backtester connected.");
            _cancellation = new CancellationTokenSource();
            _cancellation.Token.Register(() => _startTcs?.TrySetCanceled());
            _consumerTask = Task.Factory.StartNew(ConsumeRequests, _cancellation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current);
            _consumerTask.ContinueWith(t =>
            {
                var e = t.Exception ?? new Exception("Unknown exception occured");
                _startTcs?.TrySetException(e);
            }, TaskContinuationOptions.OnlyOnFaulted);

            return Task.FromResult(FakeAccountCode);
        }

        public async Task DisconnectAsync()
        {
            if (_consumerTask == null || _cancellation == null)
                return;

            if (!_cancellation.IsCancellationRequested)
            {
                _logger?.Trace($"Backtester disconnected.");
                _cancellation.Cancel();
                try
                {
                    if(_consumerTask != null && _marketDataBackgroundTask != null)
                        await Task.WhenAll(_consumerTask, _marketDataBackgroundTask);
                    else if(_consumerTask != null)
                        await _consumerTask;
                    else if (_marketDataBackgroundTask != null)
                        await _marketDataBackgroundTask;
                }
                catch (OperationCanceledException)
                {
                    _logger?.Debug("Task stopped");
                }

                _cancellation?.Dispose();
                _cancellation = null;
                _consumerTask?.Dispose();
                _consumerTask = null;
                _marketDataBackgroundTask?.Dispose();
                _marketDataBackgroundTask = null;
            }
        }

        public Task<BacktestingResults> Start()
        {
            if(_consumerTask == null)
                throw new InvalidOperationException($"Not connected");
            else if (IsDayOver())
                return Task.FromResult(_result);

            if (_startTcs == null)
                _logger?.Debug($"Backtester started (from {_start} to {_end})");
            else
                _logger?.Debug($"Backtester resumed at {_currentTime}");

            _startTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            _totalRuntimeStopwatch.Start();
            _isRunning = true;
            return _startTcs.Task;
        }

        public void Stop()
        {
            if(_isRunning)
            {
                _totalRuntimeStopwatch.Stop();
                _logger?.Debug($"Backtester stopped at {_currentTime}");
                _startTcs?.TrySetException(new BacktesterStoppedException($"Backtester stopped at {_currentTime}"));
            }
            _isRunning = false;
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

        void ConsumeRequests()
        {
            CancellationToken mainToken = _cancellation!.Token;
            while (!mainToken.IsCancellationRequested)
            {
                mainToken.ThrowIfCancellationRequested();

                // Let's process the requests first 
                while (_requestsQueue.TryDequeue(out Action? action))
                {
                    mainToken.ThrowIfCancellationRequested();
                    action();
                    _logger?.Trace($"Request consumed : {action.GetMethodInfo().Name}");
                }

                if (_isRunning)
                {
                    if (!IsDayOver())
                    {
                        AdvanceTime();
                    }
                    else
                    {
                        _totalRuntimeStopwatch.Stop();
                        _result.RunTime = _totalRuntimeStopwatch.Elapsed;
                        _logger?.Debug($"Backtester finished. Runtime : {_result.RunTime}");

                        _startTcs?.TrySetResult(_result);
                        Stop();
                    }
                }
            }
        }

        void AdvanceTime()
        {
            var mainToken = _cancellation!.Token;

            if (Debugger.IsAttached)
                _logger?.Trace($"Processing time {_currentTime}");

            mainToken.ThrowIfCancellationRequested();

            ClockTick?.Invoke(_currentTime);
            mainToken.ThrowIfCancellationRequested();

            WaitOneSecond(mainToken);

            mainToken.ThrowIfCancellationRequested();

            _progress.Time = _currentTime;
            ProgressHandler?.Invoke(_progress);

            mainToken.ThrowIfCancellationRequested();
            _lastProcessedTime = _currentTime;
            _currentTime = _currentTime.AddSeconds(1);
        }

        void WaitOneSecond(CancellationToken mainToken)
        {
            var timeToWait = TimeCompression.OneSecond;
            if (timeToWait.TotalMilliseconds > 10)
            {
                Task.Delay(timeToWait).Wait(mainToken);
                return;
            }

            // µs resolution. Not always super accurate but it's good enough
            long nbMicroSecToWait = timeToWait.Ticks / 10;
            double microSecPerTick = 1000000D / Stopwatch.Frequency;

            _µsWaitStopwatch.Restart();
            Thread.SpinWait(10);
            while((long)(_µsWaitStopwatch.ElapsedTicks * microSecPerTick) < nbMicroSecToWait)
                Thread.SpinWait(10);
            _µsWaitStopwatch.Stop();
        }

        bool IsDayOver() => _currentTime >= _end;

        public async Task<IEnumerable<TData>> GetAsync<TData>(string ticker, DateTime from, DateTime to) where TData : IMarketData, new()
        {
            if (from > to)
                throw new ArgumentException($"'from' is greater than 'to'");
            
            var data = (await _broker.HistoricalDataProvider.GetHistoricalDataAsync<TData>(ticker, from, to, _cancellation!.Token))
                .OrderBy(d => d.Time)
                .Cast<TData>();

            if (_marketDataBackgroundTask == null)
            {
                _marketDataBackgroundTask = Task.Run(async () =>
                {
                    await _broker.HistoricalDataProvider.GetHistoricalDataAsync<TData>(ticker, StartTime, EndTime, _cancellation!.Token);
                }, _cancellation!.Token);
            }

            return data;
        }

        public async Task<IEnumerable<TData>> GetAsync<TData>(string ticker, DateTime dateTime) where TData : IMarketData, new()
        {
            // Retrieving data in slices of ~10 mins (rounded up to the next 10 mins)
            var upper = dateTime.AddSeconds(1);

            var key = (ticker, typeof(TData));
            if (!_timeSlicesUpperBounds.ContainsKey(key) || _timeSlicesUpperBounds[key] < dateTime)
            {
                var span = TimeSpan.FromMinutes(10);
                long ticks = (dateTime.Ticks + span.Ticks - 1) / span.Ticks;
                var aroundTenMins = new DateTime(ticks * span.Ticks, dateTime.Kind);
                
                upper = aroundTenMins;
                if (upper >= EndTime)
                    upper = EndTime;

                _timeSlicesUpperBounds[key] = upper;
            }

            var data = (await _broker.HistoricalDataProvider.GetHistoricalDataAsync<TData>(ticker, dateTime, upper, _cancellation!.Token))
                .Where(d => d.Time == dateTime)
                .Cast<TData>();

            // The rest on a background task
            if (_marketDataBackgroundTask == null)
            {
                _marketDataBackgroundTask = Task.Run(async () =>
                {
                    await _broker.HistoricalDataProvider.GetHistoricalDataAsync<TData>(ticker, upper, EndTime, _cancellation!.Token);
                }, _cancellation!.Token);
            }

            return data;
        }

        public Task<Account> GetAccountAsync()
        {
            return Task.FromResult(_fakeAccount);
        }

        internal double UpdateCommissions(Order order, double price)
        {
            double commission = GetCommission(order, price);
            _logger?.Debug($"{order} : commission : {commission:c}");

            UpdateCashBalance(-commission);
            _totalCommission += commission;
            return commission;
        }

        internal void UpdateCashBalance(double total)
        {
            _fakeAccount.CashBalances["BASE"] += total;
            _fakeAccount.CashBalances["USD"] += total;
        }

        internal void UpdateRealizedPNL(string ticker, double totalQty, double price)
        {
            Position position = Account.Positions[ticker];
            var realized = totalQty * (price - position.AverageCost);

            position.RealizedPNL += realized;
            _fakeAccount.RealizedPnL["BASE"] += realized;
            _fakeAccount.RealizedPnL["USD"] += realized;

            _logger?.Debug($"Account {_fakeAccount.Code} :  Realized PnL  : {position.RealizedPNL:c}");
        }

        internal void UpdateUnrealizedPNL(string ticker, double currentPrice)
        {
            Position position = Account.Positions[ticker];

            position.MarketPrice = currentPrice;
            position.MarketValue = currentPrice * position.PositionAmount;

            var positionValue = position.PositionAmount * position.AverageCost;
            var unrealizedPnL = position.MarketValue - positionValue;

            position.UnrealizedPNL = unrealizedPnL;
            _fakeAccount.UnrealizedPnL["USD"] = unrealizedPnL;
            _fakeAccount.UnrealizedPnL["BASE"] = unrealizedPnL;

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
    }

    public class BacktesterStoppedException : Exception
    {
        public BacktesterStoppedException(string? message) : base(message) {}
    }
}
