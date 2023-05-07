using System.Diagnostics;
using NLog.TradingBot;
using NLog;
using NUnit.Framework;
using TradingBotV2.Broker;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.Orders;
using TradingBotV2.IBKR;
using TradingBotV2.IBKR.Client;
using TradingBotV2.Tests;

namespace IBBrokerTests
{
    internal class OrderManagerTests
    {
        internal IBroker _broker;

        [OneTimeSetUp]
        public virtual async Task OneTimeSetUp()
        {
            _broker = TestsUtils.CreateBroker(TestsUtils.CreateLogger());
            await Task.CompletedTask;
        }

        [SetUp]
        public virtual async Task SetUp()
        {
            var accountCode = await _broker.ConnectAsync();
            Assert.NotNull(accountCode);
            Assert.AreEqual("DU5962304", accountCode);
        }

        [TearDown]
        public virtual async Task TearDown()
        {
            await _broker.OrderManager.CancelAllOrdersAsync();
            await Task.Delay(50);
            await _broker.OrderManager.SellAllPositionsAsync();
            await Task.Delay(50);
            await _broker.DisconnectAsync();
            await Task.Delay(50);
        }

        // TODO : add more tests

        [Test]
        public async Task PlaceOrder_ShouldSucceed()
        {
            TestsUtils.Assert.MarketIsOpen();

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
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string ticker = "GME";
            var account = await _broker.GetAccountAsync();
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
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string ticker = "GME";
            var account = await _broker.GetAccountAsync();
            var buyOrder = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5 };
            await _broker.OrderManager.PlaceOrderAsync(ticker, buyOrder);

            // Test
            var sellOrder = new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = 10 };
            Assert.ThrowsAsync<ErrorMessage>(async () => await _broker.OrderManager.PlaceOrderAsync(ticker, sellOrder));
        }

        [Test]
        public async Task ModifyOrder_ValidOrderParams_ShouldSucceed()
        {
            TestsUtils.Assert.MarketIsOpen();

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
            TestsUtils.Assert.MarketIsOpen();

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
            TestsUtils.Assert.MarketIsOpen();

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

        [Test, Order(1)]
        public async Task SellAllPositions_SellsEverything()
        {
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string[] tickers = new string[2] { "GME", "AMC" };
            foreach(string ticker in tickers)
            {
                int randomQty = new Random().Next(3, 10);
                var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = randomQty};
                await _broker.OrderManager.PlaceOrderAsync(ticker, order);
                await _broker.OrderManager.AwaitExecutionAsync(order);
            }

            // Test
            IEnumerable<OrderExecutedResult>? execResults = null;
            execResults = await _broker.OrderManager.SellAllPositionsAsync();

            // Assert
            var account = await _broker.GetAccountAsync();
            Assert.Multiple(() =>
            {
                foreach (var pos in account.Positions)
                    Assert.AreEqual(0, pos.Value.PositionAmount);
            });
        }

        [Test]
        public async Task AwaitExecution_AlreadyExecuted_Returns()
        {
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string ticker = "GME";
            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5};
            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.PreSubmitted || openOrderMsg.OrderStatus.Status == Status.Submitted);

            // Test
            await Task.Delay(500);
            var result = await _broker.OrderManager.AwaitExecutionAsync(openOrderMsg.Order);
            Assert.NotNull(result);
            Assert.AreEqual(order.Id, result.Order.Id);
        }

        [Test]
        public async Task AwaitExecution_OrderGetsFilled_Returns()
        {
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string ticker = "GME";
            var bidAsk = await GetLatestBidAskAsync(ticker);
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = 5, LmtPrice = bidAsk.Bid };
            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.PreSubmitted || openOrderMsg.OrderStatus.Status == Status.Submitted);

            // Test
            TimeSpan timeout = TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 120*1000);
            try
            {
                var task = _broker.OrderManager.AwaitExecutionAsync(openOrderMsg.Order);
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
            TestsUtils.Assert.MarketIsOpen();

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
            Assert.ThrowsAsync<ArgumentException>(async () => await _broker.OrderManager.AwaitExecutionAsync(openOrderMsg.Order));
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
    }
}
