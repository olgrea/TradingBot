using System.Collections.Concurrent;
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
        private const string FakeAccountCode = "FAKEACCOUNT123";

        bool _isConnected = false;
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

        IBClient _client;

        BlockingCollection<Action> _requestsQueue;

        Task _consumerTask;
        CancellationTokenSource _cancellation;

        DateTime _start;
        DateTime _end;

        DateTime _currentTime;

        public Backtester(DateTime date) : this(date, MarketDataUtils.MarketStartTime, MarketDataUtils.MarketEndTime) { }
        
        public Backtester(DateTime date, TimeSpan startTime, TimeSpan endTime)
        {
            _start = new DateTime(date.Date.Ticks + startTime.Ticks);
            _end = new DateTime(date.Date.Ticks + endTime.Ticks);
            _currentTime = _start;

            if (date.Date == DateTime.Now.Date)
                throw new ArgumentException("Can't backtest the current day.");

            _client = new IBClient();
            HistoricalDataProvider = new IBHistoricalDataProvider(_client);
        }

        internal (DateTime, DateTime) TimeRange => (_start, _end);
        
        internal event Action<DateTime> ClockTick;
                
        public ILiveDataProvider LiveDataProvider => throw new NotImplementedException();
        public IHistoricalDataProvider HistoricalDataProvider { get; init; }
        public IOrderManager OrderManager => throw new NotImplementedException();

        async Task StartConsumerTask()
        {
            var mainToken = _cancellation.Token;

            //_logger.Trace($"Passing time task started");
            while (!mainToken.IsCancellationRequested && _currentTime < _end)
            {
                mainToken.ThrowIfCancellationRequested();

                // Let's process the requests first 
                while(_requestsQueue.TryTake(out Action action))
                    action();

                ClockTick?.Invoke(_currentTime);
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
    }
}
