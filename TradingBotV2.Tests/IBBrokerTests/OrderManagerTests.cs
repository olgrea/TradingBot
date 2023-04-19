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

            var hdp = (IBHistoricalDataProvider)_broker.HistoricalDataProvider;
            hdp.DbPath = TestDbPath;
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDown()
        {
            await _broker.OrderManager.CancelAllOrdersAsync();
            //await _broker.SellAllPositions();
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
            OrderResult result = await _broker.OrderManager.PlaceOrderAsync(ticker, order);

            // Assert
            var orderPlacedResult = result as OrderPlacedResult;
            Assert.NotNull(orderPlacedResult);
            if(orderPlacedResult != null)
            {
                Assert.NotNull(orderPlacedResult.OrderStatus);
                Assert.IsTrue(orderPlacedResult.OrderStatus.Status == Status.PreSubmitted || orderPlacedResult.OrderStatus.Status == Status.Submitted);
            }
        }

        [Test]
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
        [Ignore("IB paper trading accounts allow shorting. Need to implement client-side validation.")]
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
            OrderPlacedResult openOrderMsg = (OrderPlacedResult)await _broker.OrderManager.PlaceOrderAsync(ticker, order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.PreSubmitted || openOrderMsg.OrderStatus.Status == Status.Submitted);

            // Test
            order.LmtPrice = 6;
            OrderPlacedResult result = (OrderPlacedResult) await _broker.OrderManager.ModifyOrderAsync(order);

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
            OrderPlacedResult openOrderMsg = (OrderPlacedResult)await _broker.OrderManager.PlaceOrderAsync(ticker, order);
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
            OrderPlacedResult openOrderMsg = (OrderPlacedResult)await _broker.OrderManager.PlaceOrderAsync(ticker, order);
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
