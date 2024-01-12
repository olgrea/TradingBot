using System.Diagnostics;
using Broker;
using Broker.IBKR.Client;
using Broker.Orders;
using IBApi;
using NLog;
using NUnit.Framework;
using Broker.Tests;
using BidAsk = Broker.MarketData.BidAsk;
using Order = Broker.Orders.Order;
using OrderStatus = Broker.Orders.OrderStatus;

namespace IBBrokerTests
{
    internal class OrderManagerTests
    {
        protected const string TickerGME = "GME";
        protected const string TickerAMC = "AMC";
        internal IBroker _broker;
        ILogger _logger;

        [OneTimeSetUp]
        public virtual async Task OneTimeSetUp()
        {
            _logger = TestsUtils.CreateLogger();
            _broker = TestsUtils.CreateBroker(_logger);
            await Task.CompletedTask;
        }

        [SetUp]
        public virtual async Task SetUp()
        {
            _logger?.PrintCurrentTestName();
            var accountCode = await _broker.ConnectAsync();
            Assert.NotNull(accountCode);
            Assert.AreEqual("DU5962304", accountCode);
            await Task.Delay(50);
        }

        [TearDown]
        public virtual async Task TearDown()
        {
            const int delay = 100;
            await Task.Delay(delay);
            await _broker.OrderManager.CancelAllOrdersAsync();
            await Task.Delay(delay);
            await _broker.OrderManager.SellAllPositionsAsync();
            await Task.Delay(delay);
            await _broker.DisconnectAsync();
            await Task.Delay(delay);
        }

        // TODO : add more tests

        [Test]
        public async Task PlaceOrder_ShouldSucceed()
        {
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string ticker = TickerGME;
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
            string ticker = TickerGME;
            var account = await _broker.GetAccountAsync();
            var balance = account.CashBalances["BASE"];
            var bidAsk = await GetLatestBidAskAsync(ticker);
            var qty = (int)Math.Round(balance/ bidAsk.Ask + 500);

            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = qty };

            // Test
            Assert.ThrowsAsync<ErrorMessageException>(async () => await _broker.OrderManager.PlaceOrderAsync(ticker, order));
        }

        [Test]
        [Ignore("IB allow shorting for paper trading accounts but not for cash accounts. Need to implement client-side validation. Move that to Trader tests.")]
        public async Task PlaceSellOrder_WhenNotEnoughPosition_ShouldFail()
        {
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string ticker = TickerGME;
            var account = await _broker.GetAccountAsync();
            var buyOrder = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5 };
            await _broker.OrderManager.PlaceOrderAsync(ticker, buyOrder);

            // Test
            var sellOrder = new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = 10 };
            Assert.ThrowsAsync<ErrorMessageException>(async () => await _broker.OrderManager.PlaceOrderAsync(ticker, sellOrder));
        }

        [Test]
        public async Task ModifyOrder_ValidOrderParams_ShouldSucceed()
        {
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string ticker = TickerGME;
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
            string ticker = TickerGME;
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
            string ticker = TickerGME;
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
            string[] tickers = new string[2] { TickerGME, TickerAMC };
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
            string ticker = TickerGME;
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
            string ticker = TickerGME;
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
            string ticker = TickerGME;
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

        [Test]
        [Ignore("Feature not working correctly.")]
        public async Task OrderCondition_TimeCondition_ActivatesOrderWhenFulfilled()
        {
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string ticker = TickerGME;
            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5};

            var currentTime = await _broker.GetServerTimeAsync();
            DateTime condTime = currentTime.AddSeconds(Debugger.IsAttached ? 60 : 10);
            order.AddTimeCondition(isMore: true, condTime);

            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.Submitted || openOrderMsg.OrderStatus.Status == Status.PreSubmitted);

            // Test
            TimeSpan timeout = TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 120 * 1000);
            try
            {
                var result = await _broker.OrderManager.AwaitExecutionAsync(order).WaitAsync(timeout);
                Assert.NotNull(result);
                Assert.AreEqual(order.Id, result.Order.Id);
                Assert.AreEqual(condTime.Ticks, result.Time.Ticks, TimeSpan.TicksPerMillisecond*1000);
            }
            catch (TimeoutException)
            {
                Assert.Inconclusive($"Order not activated within timeout value ({timeout})");
            }
        }

        [Test]
        [Ignore("Feature not working correctly.")]
        public async Task OrderCondition_PriceCondition_ActivatesOrderWhenFulfilled()
        {
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string ticker = TickerGME;
            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5};

            var bas = await GetLatestBidAskAsync(ticker);
            order.AddPriceCondition(isMore: true, bas.Ask + 0.02);

            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.Submitted || openOrderMsg.OrderStatus.Status == Status.PreSubmitted);

            // Test
            TimeSpan timeout = TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 120 * 1000);
            try
            {
                var result = await _broker.OrderManager.AwaitExecutionAsync(order).WaitAsync(timeout);
                Assert.NotNull(result);
                Assert.AreEqual(order.Id, result.Order.Id);
            }
            catch (TimeoutException)
            {
                Assert.Inconclusive($"Order not activated within timeout value ({timeout})");
            }
        }

        [Test]
        [Ignore("Feature not working correctly.")]
        public async Task OrderCondition_PercentCondition_ActivatesOrderWhenFulfilled()
        {
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string ticker = TickerGME;
            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5};

            var bas = await GetLatestBidAskAsync(ticker);
            order.AddPercentCondition(isMore: true, 0.001);

            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.Submitted || openOrderMsg.OrderStatus.Status == Status.PreSubmitted);

            // Test
            TimeSpan timeout = TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 120 * 1000);
            try
            {
                var result = await _broker.OrderManager.AwaitExecutionAsync(order).WaitAsync(timeout);
                Assert.NotNull(result);
                Assert.AreEqual(order.Id, result.Order.Id);
            }
            catch (TimeoutException)
            {
                Assert.Inconclusive($"Order not activated within timeout value ({timeout})");
            }
        }

        [Test]
        [Ignore("Feature not working correctly.")]
        public async Task OrderCondition_TimeCondition_CancelsOrderWhenFulfilled()
        {
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string ticker = TickerGME;
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = 5, LmtPrice = 5 };

            var tcs = new TaskCompletionSource<Order>(TaskCreationOptions.RunContinuationsAsynchronously);
            var orderPlaced = new Action<string, Order, OrderStatus>((t, o, os) =>
            {
                if (o.Id == order.Id && (os.Status == Status.Cancelled || os.Status == Status.ApiCancelled))
                    tcs.TrySetResult(o);
            });
            var error = new Action<Exception>(e => tcs.TrySetException(e));
            _broker.OrderManager.OrderUpdated += orderPlaced;
            _broker.ErrorOccured += error;

            var currentTime = await _broker.GetServerTimeAsync();
            DateTime condTime = currentTime.AddSeconds(Debugger.IsAttached ? 60 : 10);
            order.AddTimeCondition(isMore: true, condTime);
            order.ConditionsTriggerOrderCancellation = true;

            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.Submitted);

            // Test
            TimeSpan timeout = TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 120 * 1000);
            try
            {
                var result = await tcs.Task.WaitAsync(timeout);
                Assert.NotNull(result);
                Assert.AreEqual(order.Id, result.Id);
            }
            catch (TimeoutException)
            {
                _broker.OrderManager.OrderUpdated -= orderPlaced;
                _broker.ErrorOccured -= error;
                Assert.Inconclusive($"Order not cancelled within timeout value ({timeout})");
            }
        }

        [Test]
        [Ignore("Feature not working correctly.")]
        public async Task OrderCondition_PriceCondition_CancelsOrderWhenFulfilled()
        {
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string ticker = TickerGME;
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = 5, LmtPrice = 5 };

            var tcs = new TaskCompletionSource<Order>(TaskCreationOptions.RunContinuationsAsynchronously);
            var orderPlaced = new Action<string, Order, OrderStatus>((t, o, os) =>
            {
                if (o.Id == order.Id && (os.Status == Status.Cancelled || os.Status == Status.ApiCancelled))
                    tcs.TrySetResult(o);
            });
            var error = new Action<Exception>(e => tcs.TrySetException(e));
            _broker.ErrorOccured += error;
            _broker.OrderManager.OrderUpdated += orderPlaced;

            var bas = await GetLatestBidAskAsync(ticker);
            order.AddPriceCondition(isMore: true, bas.Ask + 0.02);
            order.ConditionsTriggerOrderCancellation = true;

            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.Submitted);

            // Test
            TimeSpan timeout = TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 120 * 1000);
            try
            {
                var result = await tcs.Task.WaitAsync(timeout);
                Assert.NotNull(result);
                Assert.AreEqual(order.Id, result.Id);
            }
            catch (TimeoutException)
            {
                _broker.OrderManager.OrderUpdated -= orderPlaced;
                _broker.ErrorOccured -= error;
                Assert.Inconclusive($"Order not cancelled within timeout value ({timeout})");
            }
        }

        [Test]
        [Ignore("Feature not working correctly.")]
        public async Task OrderCondition_PercentCondition_CancelsOrderWhenFulfilled()
        {
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string ticker = TickerGME;
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = 5, LmtPrice = 5 };

            var tcs = new TaskCompletionSource<Order>(TaskCreationOptions.RunContinuationsAsynchronously);
            var orderPlaced = new Action<string, Order, OrderStatus>((t, o, os) =>
            {
                if (o.Id == order.Id && (os.Status == Status.Cancelled || os.Status == Status.ApiCancelled))
                    tcs.TrySetResult(o);
            });
            var error = new Action<Exception>(e => tcs.TrySetException(e));
            _broker.OrderManager.OrderUpdated += orderPlaced;
            _broker.ErrorOccured += error;

            var bas = await GetLatestBidAskAsync(ticker);
            order.AddPercentCondition(isMore: true, 0.001);
            order.ConditionsTriggerOrderCancellation = true;

            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.Submitted);

            // Test
            TimeSpan timeout = TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 120 * 1000);
            try
            {
                var result = await tcs.Task.WaitAsync(timeout);
                Assert.NotNull(result);
                Assert.AreEqual(order.Id, result.Id);
            }
            catch (TimeoutException)
            {
                _broker.OrderManager.OrderUpdated -= orderPlaced;
                _broker.ErrorOccured -= error;
                Assert.Inconclusive($"Order not cancelled within timeout value ({timeout})");
            }
        }

        [Test]
        [Ignore("Feature not working correctly.")]
        public async Task OrderCondition_Conjunction()
        {
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string ticker = TickerGME;
            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5};

            var bas = await GetLatestBidAskAsync(ticker);
            order.AddPriceCondition(isMore: true, bas.Ask + 0.02, isConjunction: true);

            var currentTime = await _broker.GetServerTimeAsync();
            DateTime condTime = currentTime.AddSeconds(Debugger.IsAttached ? 60 : 10);
            order.AddTimeCondition(isMore: true, condTime);

            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.Submitted || openOrderMsg.OrderStatus.Status == Status.PreSubmitted);

            // Test
            TimeSpan timeout = TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 120 * 1000);
            try
            {
                var result = await _broker.OrderManager.AwaitExecutionAsync(order).WaitAsync(timeout);
                Assert.NotNull(result);
                Assert.AreEqual(order.Id, result.Order.Id);
            }
            catch (TimeoutException)
            {
                Assert.Inconclusive($"Order not activated within timeout value ({timeout})");
            }
        }

        [Test]
        [Ignore("Feature not working correctly.")]
        public async Task OrderCondition_Disjunction()
        {
            TestsUtils.Assert.MarketIsOpen();

            // Setup
            string ticker = TickerGME;
            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5 };

            var bas = await GetLatestBidAskAsync(ticker);
            order.AddPriceCondition(isMore: true, bas.Ask + 0.02, isConjunction: false);

            var currentTime = await _broker.GetServerTimeAsync();
            DateTime condTime = currentTime.AddSeconds(Debugger.IsAttached ? 60 : 10);
            order.AddTimeCondition(isMore: true, condTime);

            OrderPlacedResult openOrderMsg = await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.Submitted || openOrderMsg.OrderStatus.Status == Status.PreSubmitted);

            // Test
            TimeSpan timeout = TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 120 * 1000);
            try
            {
                var result = await _broker.OrderManager.AwaitExecutionAsync(order).WaitAsync(timeout);
                Assert.NotNull(result);
                Assert.AreEqual(order.Id, result.Order.Id);
            }
            catch (TimeoutException)
            {
                Assert.Inconclusive($"Order not activated within timeout value ({timeout})");
            }
        }

        [Test, Explicit]
        public async Task MarketOnOpenOrder_BecomesActiveOnMarketOpen()
        {
            // Setup
            string ticker = TickerGME;
            var order = new MarketOnOpen() { Action = OrderAction.BUY, TotalQuantity = 5};
            await _broker.OrderManager.PlaceOrderAsync(ticker, order);
        }

        [Test, Explicit]
        public async Task AdaptiveAlgo()
        {
            // Setup
            string ticker = TickerGME;
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = 5, LmtPrice = 1 };
            order.AsAdaptiveAlgo(AdaptiveAlgorithmPriority.Urgent);

            await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            await _broker.OrderManager.AwaitExecutionAsync(order);
        }

        async Task<BidAsk> GetLatestBidAskAsync(string ticker)
        {
            var tcs = new TaskCompletionSource<BidAsk>(TaskCreationOptions.RunContinuationsAsynchronously);
            var error = new Action<Exception>(e => tcs.TrySetException(e));
            var callback = new Action<string , BidAsk>((t, ba) =>
            {
                if (t == ticker)
                    tcs.TrySetResult(ba);
            });

            try
            {
                _broker.LiveDataProvider.BidAskReceived += callback;
                _broker.ErrorOccured += error;
                _broker.LiveDataProvider.RequestBidAskUpdates(ticker);
                return await tcs.Task;
            }
            finally
            {
                _broker.LiveDataProvider.CancelBidAskUpdates(ticker);
                _broker.LiveDataProvider.BidAskReceived -= callback;
                _broker.ErrorOccured -= error;
            }
        }
    }
}
