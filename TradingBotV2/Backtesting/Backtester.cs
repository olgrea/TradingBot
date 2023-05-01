using System.Collections.Concurrent;
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
    internal class Backtester : IBroker, IAsyncDisposable
    {
        static class TimeCompression
        {
            public static double Factor = 0.001;
            public static int OneSecond => (int)Math.Round(1 * 1000 * Factor);
        }

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
        ILogger? _logger;
        ConcurrentQueue<Action> _requestsQueue = new ConcurrentQueue<Action>();

        Task? _consumerTask;
        Task? _marketDataBackgroundTask;
        CancellationTokenSource? _cancellation;
        TaskCompletionSource? _tcsDayIsOver;

        DateTime _start;
        DateTime _end;
        DateTime _currentTime;
        BacktesterProgress _progress = new BacktesterProgress();

        public Backtester(DateTime date, ILogger? logger = null) : this(date.ToMarketHours().Item1, date.ToMarketHours().Item2, logger) { }

        public Backtester(DateTime from, DateTime to, ILogger? logger = null)
        {
            if(to - from > TimeSpan.FromDays(1))
                throw new ArgumentException("Can only backtest a single day.");

            if (from.Date == DateTime.Now.Date || to.Date == DateTime.Now.Date)
                throw new ArgumentException("Can't backtest the current day.");

            _start = from;
            _end = to;
            _currentTime = _start;
            _logger = logger;

            _broker = new IBBroker(191919, logger);
            LiveDataProvider = new BacktesterLiveDataProvider(this);
            HistoricalDataProvider = new IBHistoricalDataProvider(_broker, logger);
            OrderManager = new BacktesterOrderManager(this);
        }

        public async ValueTask DisposeAsync()
        {
            await Stop();
            await DisconnectAsync();
            _cancellation?.Dispose();
            _consumerTask?.Dispose();
            _marketDataBackgroundTask?.Dispose();
            if (_broker != null )
                await _broker.DisconnectAsync();
        }

        internal DateTime StartTime => _start;
        internal DateTime EndTime => _end;
        internal DateTime CurrentTime => _currentTime;
        internal ILogger? Logger => _logger;
        internal Account Account => _fakeAccount;

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

        internal event Action<DateTime>? ClockTick;
        public event Action<Account>? AccountUpdated;
        public Action<BacktesterProgress>? ProgressHandler { get; set; }

        public Task<string> ConnectAsync()
        {
            if (_consumerTask != null)
                throw new ErrorMessage("Already connected");

            _cancellation = new CancellationTokenSource();
            _cancellation.Token.Register(() => _tcsDayIsOver?.TrySetCanceled());
            _consumerTask = Task.Factory.StartNew(ConsumeRequests, _cancellation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current);
            _consumerTask.ContinueWith(t =>
            {
                var e = t.Exception ?? new Exception("Unknown exception occured");
                _tcsDayIsOver?.TrySetException(e);
            }, TaskContinuationOptions.OnlyOnFaulted);

            return Task.FromResult(FakeAccountCode);
        }

        public async Task DisconnectAsync()
        {
            if (!_cancellation!.IsCancellationRequested)
            {
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

        public Task Start()
        {
            if (IsDayOver())
                throw new InvalidOperationException($"Backtesting of {_currentTime.ToShortDateString()} is already completed.");
            else if(_consumerTask == null)
                throw new InvalidOperationException($"Not connected");

            if (_tcsDayIsOver == null)
                _tcsDayIsOver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            return _tcsDayIsOver.Task;
        }

        public async Task Stop()
        {
            _tcsDayIsOver?.TrySetCanceled();
            _tcsDayIsOver = null;
            await Task.CompletedTask;
        }

        public async Task Reset()
        {
            await Stop();

            _requestsQueue.Clear();
            _currentTime = _start;

            LiveDataProvider.Dispose();
            LiveDataProvider = new BacktesterLiveDataProvider(this);
        }

        internal void EnqueueRequest(Action action)
        {
            if (_consumerTask == null)
                throw new InvalidOperationException($"Consumer task not started.");

            if (_consumerTask.IsFaulted || _consumerTask.IsCanceled)
                _consumerTask.Wait();

            _requestsQueue.Enqueue(action);
        }

        void ConsumeRequests()
        {
            var mainToken = _cancellation!.Token;

            while (!mainToken.IsCancellationRequested)
            {
                mainToken.ThrowIfCancellationRequested();

                // Let's process the requests first 
                while (_requestsQueue.TryDequeue(out Action? action))
                {
                    mainToken.ThrowIfCancellationRequested();
                    action();
                }

                if(_tcsDayIsOver != null)
                {
                    if (!IsDayOver())
                        AdvanceTime();
                    else
                        _tcsDayIsOver?.TrySetResult();
                }
            }
        }

        void AdvanceTime()
        {
            var mainToken = _cancellation!.Token;

            mainToken.ThrowIfCancellationRequested();
            ClockTick?.Invoke(_currentTime);
            mainToken.ThrowIfCancellationRequested();

            if (TimeCompression.OneSecond > 0)
                Task.Delay(TimeCompression.OneSecond).Wait(mainToken);

            _currentTime = _currentTime.AddSeconds(1);

            mainToken.ThrowIfCancellationRequested();

            _progress.CurrentTime = _currentTime;
            ProgressHandler?.Invoke(_progress);
            mainToken.ThrowIfCancellationRequested();
        }

        bool IsDayOver() => _currentTime >= _end;

        public async Task<IEnumerable<TData>> GetAsync<TData>(string ticker, DateTime from, DateTime to) where TData : IMarketData, new()
        {
            if (from > to)
                throw new ArgumentException($"'from' is greater than 'to'");

            DateTime current = from;
            IEnumerable<TData> results = Enumerable.Empty<TData>();
            while (current < to)
            {
                var data = await GetAsync<TData>(ticker, current);
                results = results.Concat(data);
                current = current.AddSeconds(1);
            }

            return results;
        }

        public async Task<IEnumerable<TData>> GetAsync<TData>(string ticker, DateTime dateTime) where TData : IMarketData, new()
        {
            var data = (await _broker.HistoricalDataProvider.GetHistoricalDataAsync<TData>(ticker, dateTime, dateTime.AddSeconds(1), _cancellation!.Token))
                .OrderBy(d => d.Time)
                .Cast<TData>();
            
            if (_marketDataBackgroundTask == null)
            {
                // Retrieving 10 mins right now and the rest on a background task
                await _broker.HistoricalDataProvider.GetHistoricalDataAsync<TData>(ticker, dateTime, dateTime.AddMinutes(10), _cancellation!.Token);
                _marketDataBackgroundTask = Task.Run(async () =>
                {
                    await _broker.HistoricalDataProvider.GetHistoricalDataAsync<TData>(ticker, StartTime, EndTime, _cancellation!.Token);
                }, _cancellation!.Token);
            }

            return data;
        }

        public Task<Account> GetAccountAsync(string accountCode)
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

        public void RequestAccountUpdates(string account)
        {
            throw new NotImplementedException();
        }

        public void CancelAccountUpdates(string account)
        {
            throw new NotImplementedException();
        }
    }
}
