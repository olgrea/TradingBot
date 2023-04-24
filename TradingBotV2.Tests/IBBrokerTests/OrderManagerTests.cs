using System.Diagnostics;
using NUnit.Framework;
using TradingBotV2.Broker;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.Orders;
using TradingBotV2.IBKR;

namespace IBBrokerTests
{
    internal class OrderManagerTests
    {
        public const string TestDbPath = @"C:\tradingbot\db\tests.sqlite3";

        string _accountCode;
        internal IBroker _broker;

        [OneTimeSetUp]
        public virtual async Task OneTimeSetUp()
        {
            _broker = new IBBroker(9001);
            _accountCode = await _broker.ConnectAsync();
            Assert.NotNull(_accountCode);
            Assert.AreEqual("DU5962304", _accountCode);

            var hdp = (IBHistoricalDataProvider)_broker.HistoricalDataProvider;
            hdp.DbPath = TestDbPath;
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDown()
        {
            await _broker.OrderManager.CancelAllOrdersAsync();
            //TODO : await _broker.SellAllPositions();
            await Task.Delay(50);
            await _broker.DisconnectAsync();
            await Task.Delay(50);
        }

        // TODO : add more tests

        [Test]
        public async Task PlaceOrder_ShouldSucceed()
        {
            if (!IsMarketOpen())
                Assert.Ignore();

            // Setup
            string ticker = "GME";
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = 5, LmtPrice = 5 };

            // Test
            OrderPlacedResult orderPlacedResult = await _broker.OrderManager.PlaceOrderAsync(ticker, order);

            // Assert
            Assert.NotNull(orderPlacedResult);
            if(orderPlacedResult != null)
            {
                Assert.NotNull(orderPlacedResult.OrderStatus);
                Assert.IsTrue(orderPlacedResult.OrderStatus.Status == Status.PreSubmitted || orderPlacedResult.OrderStatus.Status == Status.Submitted);
            }
        }

        [Test]
        [Ignore("Move that to Trader tests.")]
        public async Task PlaceBuyOrder_WhenNotEnoughFunds_ShouldFail()
        {
            if (!IsMarketOpen())
                Assert.Ignore();

            // Setup
            string ticker = "GME";
            var account = await _broker.GetAccountAsync(_accountCode);
            var balance = account.CashBalances["BASE"];
            var bidAsk = await GetLatestBidAskAsync(ticker);
            var qty = (int)Math.Round(balance/ bidAsk.Ask + 500);

            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = qty };

            // Test
            Assert.ThrowsAsync<ErrorMessage>(async () => await _broker.OrderManager.PlaceOrderAsync(ticker, order));
        }

        [Test]
        [Ignore("IB allow shorting for paper trading accounts but not for cash accounts. Need to implement client-side validation. Move that to Trader tests.")]
        public async Task PlaceSellOrder_WhenNotEnoughPosition_ShouldFail()
        {
            if (!IsMarketOpen())
                Assert.Ignore();

            // Setup
            string ticker = "GME";
            var account = await _broker.GetAccountAsync(_accountCode);
            var buyOrder = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5 };
            await _broker.OrderManager.PlaceOrderAsync(ticker, buyOrder);

            // Test
            var sellOrder = new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = 10 };
            Assert.ThrowsAsync<ErrorMessage>(async () => await _broker.OrderManager.PlaceOrderAsync(ticker, sellOrder));
        }

        [Test]
        public async Task ModifyOrder_ValidOrderParams_ShouldSucceed()
        {
            if (!IsMarketOpen())
                Assert.Ignore();

            // Setup
            string ticker = "GME";
            int randomQty = new Random().Next(3, 10);
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = randomQty, LmtPrice = 5 };
            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.PreSubmitted || openOrderMsg.OrderStatus.Status == Status.Submitted);

            // Test
            order.LmtPrice = 6;
            OrderPlacedResult result = await _broker.OrderManager.ModifyOrderAsync(order);

            // Assert
            Assert.NotNull(result?.OrderStatus);
            if(result != null)
                Assert.IsTrue(result.OrderStatus.Status == Status.PreSubmitted || result.OrderStatus.Status == Status.Submitted);
        }

        [Test]
        public async Task CancelOrder_ShouldSucceed()
        {
            if (!IsMarketOpen())
                Assert.Ignore();

            // Setup
            string ticker = "GME";
            int randomQty = new Random().Next(3, 10);
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = randomQty, LmtPrice = 5 };
            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.PreSubmitted || openOrderMsg.OrderStatus.Status == Status.Submitted);

            // Test
            OrderStatus orderStatus = await _broker.OrderManager.CancelOrderAsync(openOrderMsg.Order.Id);

            // Assert
            Assert.NotNull(orderStatus);
            Assert.IsTrue(orderStatus.Status == Status.Cancelled);
        }

        [Test]
        public async Task CancelOrder_AlreadyCanceled_Throws()
        {
            if (!IsMarketOpen())
                Assert.Ignore();

            // Setup
            string ticker = "GME";
            int randomQty = new Random().Next(3, 10);
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = randomQty, LmtPrice = 5 };
            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.PreSubmitted || openOrderMsg.OrderStatus.Status == Status.Submitted);

            // Test
            OrderStatus orderStatus = await _broker.OrderManager.CancelOrderAsync(openOrderMsg.Order.Id);
            Assert.NotNull(orderStatus);
            Assert.IsTrue(orderStatus.Status == Status.Cancelled);

            // Assert
            Assert.ThrowsAsync<ArgumentException>(async () => await _broker.OrderManager.CancelOrderAsync(openOrderMsg.Order.Id));
        }

        [Test]
        public async Task AwaitExecution_AlreadyExecuted_Returns()
        {
            if (!IsMarketOpen())
                Assert.Ignore();

            // Setup
            string ticker = "GME";
            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5};
            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.PreSubmitted || openOrderMsg.OrderStatus.Status == Status.Submitted);

            // Test
            var result = await _broker.OrderManager.AwaitExecution(openOrderMsg.Order);
            Assert.NotNull(result);
            Assert.AreEqual(order.Id, result.Order.Id);
        }

        [Test]
        public async Task AwaitExecution_OrderGetsFilled_Returns()
        {
            if (!IsMarketOpen())
                Assert.Ignore();

            // Setup
            string ticker = "GME";
            var bidAsk = await GetLatestBidAskAsync(ticker);
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = 5, LmtPrice = bidAsk.Bid };
            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.PreSubmitted || openOrderMsg.OrderStatus.Status == Status.Submitted);

            // Test
            TimeSpan timeout = TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 60*1000);
            try
            {
                var task = _broker.OrderManager.AwaitExecution(openOrderMsg.Order);
                OrderExecutedResult result = await task.WaitAsync(timeout);
                Assert.NotNull(result);
                Assert.AreEqual(order.Id, result.Order.Id);
            }
            catch (TimeoutException)
            {
                Assert.Inconclusive($"Order not filled within timeout value ({timeout})");
            }
        }

        [Test]
        public async Task AwaitExecution_OrderGetsCancelled_Throws()
        {
            if (!IsMarketOpen())
                Assert.Ignore();

            // Setup
            string ticker = "GME";
            var bidAsk = await GetLatestBidAskAsync(ticker);
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = 5, LmtPrice = bidAsk.Bid - 5 };
            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.PreSubmitted || openOrderMsg.OrderStatus.Status == Status.Submitted);

            OrderStatus status = await _broker.OrderManager.CancelOrderAsync(order.Id);
            Assert.NotNull(status);
            Assert.IsTrue(status.Status == Status.Cancelled || status.Status == Status.ApiCancelled);

            // Test
            Assert.ThrowsAsync<ArgumentException>(async () => await _broker.OrderManager.AwaitExecution(openOrderMsg.Order));
        }

        async Task<BidAsk> GetLatestBidAskAsync(string ticker)
        {
            var tcs = new TaskCompletionSource<BidAsk>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new Action<string , BidAsk>((t, ba) =>
            {
                if (t == ticker)
                    tcs.TrySetResult(ba);
            });

            _broker.LiveDataProvider.BidAskReceived += callback;
            try
            {
                _broker.LiveDataProvider.RequestBidAskUpdates(ticker);
                return await tcs.Task;
            }
            finally
            {
                _broker.LiveDataProvider.CancelBidAskUpdates(ticker);
                _broker.LiveDataProvider.BidAskReceived -= callback;
            }
        }

        protected virtual bool IsMarketOpen()
        {
            return MarketDataUtils.IsMarketOpen();
        }
    }
}
