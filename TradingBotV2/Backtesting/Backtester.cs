using System.Collections.Concurrent;
using System.Diagnostics;
using NLog;
using TradingBotV2.Broker;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.MarketData.Providers;
using TradingBotV2.Broker.Orders;
using TradingBotV2.IBKR;

namespace TradingBotV2.Backtesting
{
    internal class Backtester : IBroker
    {
        static class TimeDelays
        {
            public static double TimeScale = 0.001;
            public static int OneSecond => (int)Math.Round(1 * 1000 * TimeScale);
        }

        private const string FakeAccountCode = "FAKEACCOUNT123";
        Account _fakeAccount = new Account()
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

        bool _isConnected = false;
        double _totalCommission = 0.0;

        IBBroker _broker;
        ILogger _logger;
        IHistoricalDataProvider _historicalDataProvider;
        ConcurrentQueue<Action> _requestsQueue;

        Task _consumerTask;
        CancellationTokenSource _cancellation;

        DateTime _start;
        DateTime _end;
        DateTime _currentTime;

        public Backtester(DateTime date, ILogger logger = null) : this(date, MarketDataUtils.MarketStartTime, MarketDataUtils.MarketEndTime, logger) { }

        public Backtester(DateTime date, TimeSpan startTime, TimeSpan endTime, ILogger logger = null)
        {
            _start = new DateTime(date.Date.Ticks + startTime.Ticks);
            _end = new DateTime(date.Date.Ticks + endTime.Ticks);
            _currentTime = _start;
            _logger = logger;

            if (date.Date == DateTime.Now.Date)
                throw new ArgumentException("Can't backtest the current day.");

            _broker = new IBBroker(191919, logger);
            LiveDataProvider = new BacktesterLiveDataProvider(this);
            _historicalDataProvider = new IBHistoricalDataProvider(_broker.Client, logger);
        }

        ~Backtester()
        {
            _broker?.DisconnectAsync().Wait();
        }

        internal DateTime StartTime => _start;
        internal DateTime EndTime => _end;
        internal DateTime CurrentTime => _currentTime;
        internal (DateTime, DateTime) TimeRange => (_start, _end);
        internal ILogger Logger => _logger;
        internal ConcurrentQueue<Action> RequestsQueue => _requestsQueue;
        internal Account Account => _fakeAccount;

        internal event Action<DateTime> ClockTick;
                
        public ILiveDataProvider LiveDataProvider { get; init; }
        public IHistoricalDataProvider HistoricalDataProvider 
        {
            get
            {
                if (!_broker.IsConnected())
                    _broker.ConnectAsync().Wait();
                return _historicalDataProvider;
            } 
        }
        public IOrderManager OrderManager => throw new NotImplementedException();

        public Task Start()
        {
            if(_consumerTask == null)
            {
                _requestsQueue = new ConcurrentQueue<Action>();
                _cancellation = new CancellationTokenSource();
                _consumerTask = Task.Factory.StartNew(PassTime, _cancellation.Token);
            }

            return _consumerTask;
        }

        public void Stop()
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
            _consumerTask = null;
        }

        public void Reset()
        {
            Stop();
            _currentTime = _start;
            (LiveDataProvider as BacktesterLiveDataProvider)?.Reset();
        }

        void PassTime()
        {
            var mainToken = _cancellation.Token;

            //_logger.Trace($"Passing time task started");
            while (!mainToken.IsCancellationRequested && _currentTime < _end)
            {
                mainToken.ThrowIfCancellationRequested();

                // Let's process the requests first 
                while(_requestsQueue.TryDequeue(out Action action))
                    action();

                ClockTick?.Invoke(_currentTime);
                if (TimeDelays.OneSecond > 0)
                    Task.Delay(TimeDelays.OneSecond).Wait(mainToken);
                _currentTime = _currentTime.AddSeconds(1);
            }
        }

        public Task<string> ConnectAsync()
        {
            if (_isConnected)
                throw new ErrorMessage("Already connected");

            _isConnected = true;
            return Task.FromResult<string>(FakeAccountCode);
        }

        public Task DisconnectAsync()
        {
            _isConnected = false;
            return Task.CompletedTask;
        }

        public Task<Account> GetAccountAsync(string accountCode)
        {
            return Task.FromResult(_fakeAccount);
        }

        internal double UpdateCommissions(Order order, double price)
        {
            double commission = GetCommission(order, price);
            _logger.Debug($"{order} : commission : {commission:c}");

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

            _logger.Debug($"Account {_fakeAccount.Code} :  Realized PnL  : {position.RealizedPNL:c}");
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
}
