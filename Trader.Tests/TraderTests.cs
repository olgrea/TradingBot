using System.Diagnostics;
using Broker.IBKR;
using Broker.IBKR.Orders;
using Broker.MarketData;
using Broker.Tests;
using NLog;
using NUnit.Framework;
using Trader.Indicators;
using Trader.Strategies;

namespace TraderTests
{
    internal class TraderTests
    {
        const string Ticker = "GME";
        ILogger _logger;
        IIBBroker _broker;
        Trader.Trader _trader;
        TestStrategy _testStrategy;

        [SetUp]
        public void SetUp()
        {
            _logger = TestsUtils.CreateLogger();
            _broker = TestsUtils.CreateBroker();
            _trader = new Trader.Trader(_broker, _logger);

            var now = DateTime.Now;
            var later = now.AddSeconds(60);

            _testStrategy = new TestStrategy(now, later, Ticker, _trader);
            _trader.AddStrategy(_testStrategy);
        }

        [TearDown]
        public void TearDown()
        {
            _trader.Stop();
        }

        [Test]
        public async Task Start_WhenConnectionLost_SellsPositions()
        {
            TestsUtils.Assert.MarketIsOpen();

            await _broker.ConnectAsync();
            IBOrderPlacedResult placedOrder = (IBOrderPlacedResult)await _broker.OrderManager.PlaceOrderAsync(Ticker, new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5 });
            await _broker.OrderManager.AwaitExecutionAsync(placedOrder.Order);
            await _broker.DisconnectAsync();

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var orderExecuted = new Action<string, IBOrderExecution>((ticker, oe) => 
            {
                if (ticker == Ticker && oe.Shares >= 5)
                    tcs.TrySetResult();
            });

            try
            {
                _broker.OrderManager.OrderExecuted += orderExecuted;
                var task = _trader.Start();
                await Task.Delay(1000);
                await ResetNetworkAdapter();
                await tcs.Task;
            }
            finally
            {
                _broker.OrderManager.OrderExecuted -= orderExecuted;
            }
        }

        [Test]
        public async Task Start_WhenConnectionLost_CancelAllOrders()
        {
            TestsUtils.Assert.MarketIsOpen();

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var orderExecuted = new Action<string, IBOrderExecution>((ticker, oe) =>
            {
                if (ticker == Ticker)
                {
                    Assert.Inconclusive("Order wasn't meant to be executed.");
                    tcs.TrySetCanceled();
                }
            });

            await _broker.ConnectAsync();
            _broker.OrderManager.OrderExecuted += orderExecuted;
            var price = 5;
            var placedOrder = await _broker.OrderManager.PlaceOrderAsync(Ticker, new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = 5, LmtPrice = price });
            await _broker.DisconnectAsync();

            var orderStatusChanged = new Action<string, IBOrder, IBOrderStatus>((ticker, o, os) =>
            {
                if (ticker == Ticker && (os.Status == Status.Cancelled || os.Status == Status.ApiCancelled))
                {
                    tcs.TrySetResult();
                }
            });
            
            try
            {
                _broker.OrderManager.OrderUpdated += orderStatusChanged;
                var task = _trader.Start();
                await Task.Delay(1000);
                await ResetNetworkAdapter();
                await tcs.Task;
            }
            finally
            {
                _broker.OrderManager.OrderUpdated -= orderStatusChanged;
                _broker.OrderManager.OrderExecuted -= orderExecuted;
            }
        }

        [Test]
        public async Task Start_WhenConnectionLost_RestartsStrategy()
        {
            TestsUtils.Assert.MarketIsOpen();

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var strategyExecuted = new Action<DateTime>(time => tcs.TrySetResult());

            try
            {
                var task = _trader.Start();
                await Task.Delay(2000);
                await ResetNetworkAdapter();
                _testStrategy.StrategyExecuted += strategyExecuted;
                await tcs.Task;
            }
            finally
            {
                _testStrategy.StrategyExecuted -= strategyExecuted;
            }
        }

        async Task ResetNetworkAdapter()
        {
            TimeSpan timeout = TimeSpan.FromSeconds(10);
            var proc = Process.Start("ipconfig", "/release");
            await proc.WaitForExitAsync().WaitAsync(timeout);
            proc = Process.Start("ipconfig", "/renew");
            await proc.WaitForExitAsync().WaitAsync(timeout);
        }

        class TestStrategy : StrategyBase
        {
            public TestStrategy(DateTime startTime, DateTime endTime, string ticker, Trader.Trader trader) : base(startTime, endTime, ticker, trader)
            {
                Indicators = new List<IIndicator>() { new BollingerBands(BarLength._5Sec) };
            }

            internal event Action<DateTime> StrategyExecuted;
            public override IEnumerable<IIndicator> Indicators { get; init; }

            protected override Task ExecuteStrategy(DateTime time)
            {
                if (time >= EndTime.AddSeconds(-5))
                    _executeStrategyBlock.Complete();

                StrategyExecuted?.Invoke(time);
                return Task.CompletedTask;
            }
            protected override void CancelMarketData() 
            {
                _trader.Broker.LiveDataProvider.BarReceived -= OnBarReceived;
                _trader.Broker.LiveDataProvider.CancelBarUpdates(_ticker, BarLength._5Sec);
            }

            protected override void RequestMarketData()
            {
                _trader.Broker.LiveDataProvider.BarReceived += OnBarReceived;
                _trader.Broker.LiveDataProvider.RequestBarUpdates(_ticker, BarLength._5Sec);
            }

            private void OnBarReceived(string ticker, Bar bar)
            {
                if(ticker == _ticker && bar.BarLength == BarLength._5Sec)
                {
                    _executeStrategyBlock.Post(bar.Time);
                }
            }
        }
    }
}
