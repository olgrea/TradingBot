using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLog;
using NUnit.Framework;
using TradingBot.Broker;
using TradingBot.Broker.Client;
using TradingBot.Broker.Client.Messages;
using TradingBot.Broker.Orders;
using TradingBot.Utils;

namespace Tests.Broker
{
    [TestFixture]
    internal class IBBrokerTests
    {
        protected IBBroker _broker;
        protected ConnectMessage _connectMessage;

        [OneTimeSetUp]
        public virtual async Task OneTimeSetUp()
        {
            _broker = new IBBroker(191919);
            await Task.CompletedTask;
        }

        [SetUp]
        public virtual async Task SetUp()
        {
            _connectMessage = await _broker.ConnectAsync();
            Assert.IsTrue(_connectMessage.AccountCode == "DU5962304");
        }

        [TearDown]
        public async Task TearDown()
        {
            _broker.CancelAllOrders();
            await Task.Delay(50);
            await _broker.DisconnectAsync();
            await Task.Delay(50);
        }

        [Test]
        public async Task ConnectAsync_OnSuccess_ReturnsRelevantConnectionInfo()
        {
            // Setup
            await _broker.DisconnectAsync();
            await Task.Delay(50);

            // Test
            var msg = await _broker.ConnectAsync();

            // Assert
            Assert.IsNotNull(msg);
            Assert.IsTrue(msg.NextValidOrderId > 0);
            Assert.IsNotNull(msg.AccountCode);
        }

        [Test]
        public async Task ConnectAsync_AlreadyConnected_ThrowsError()
        {
            // Setup
            await _broker.DisconnectAsync();
            await Task.Delay(50);
            await _broker.ConnectAsync();
            
            // Test
            Assert.ThrowsAsync<ErrorMessage>(async () => await _broker.ConnectAsync()); 
        }

        [Test]
        public async Task ConnectAsync_CanBeCancelled()
        {
            // Setup
            await _broker.DisconnectAsync();
            await Task.Delay(50);

            // Test
            var source = new CancellationTokenSource(5);
            Assert.ThrowsAsync<TaskCanceledException>(async () => await _broker.ConnectAsync(source.Token));
        }

        [Test]
        public async Task RequestAccountUpdates_ReceivesAccount()
        {
            // Setup
            string accountReceived = null;
            var tcs = new TaskCompletionSource<string>();
            var callback = new Action<string, string, string, string>( (key, value, currency, acc) => tcs.SetResult(acc));
            _broker.AccountValueUpdated += callback;

            // Test
            try
            {
                _broker.RequestAccountUpdates(_connectMessage.AccountCode);
                accountReceived = await tcs.Task;
            }
            finally
            {
                _broker.CancelAccountUpdates(_connectMessage.AccountCode);
                _broker.AccountValueUpdated -= callback;
            }

            // Assert
            Assert.AreEqual(_connectMessage.AccountCode, accountReceived);
        }

        [Test]
        public async Task GetNextValidIdAsync_ReturnsId()
        {
            // Test
            var id = await _broker.GetNextValidOrderIdAsync();
            Assert.IsTrue(id > 0);
        }

        [Test]
        public async Task GetContractDetailsAsync_WithValidInput_ReturnsContractDetails()
        {
            // Setup
            var dummy = MakeDummyContract("GME");

            // Test
            var details = await _broker.GetContractDetailsAsync(dummy);

            // Assert
            Assert.NotNull(details);
            Assert.IsNotEmpty(details);

            var contract = details.First().Contract;
            Assert.NotNull(contract);
            Assert.IsTrue(contract.Id > 0);
            Assert.AreEqual(dummy.Symbol, contract.Symbol);
            Assert.AreEqual(dummy.SecType, contract.SecType);
        }

        [Test]
        public async Task GetContractDetailsAsync_WithInvalidInput_Throws()
        {
            // Setup
            var dummy = MakeDummyContract("GMEdasdafafsafaf");

            // Test
            Assert.ThrowsAsync<ErrorMessage>(async () => await _broker.GetContractDetailsAsync(dummy));
            await Task.CompletedTask;
        }

        [Test]
        public async Task PlaceOrder_WithOrderIdNotSet_Throws()
        {
            // Setup
            var contract = await GetContract("GME");
            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5 };

            // Test
            Assert.ThrowsAsync<ArgumentException>(async () => await _broker.PlaceOrderAsync(contract, order));
        }

        [Test]
        public async Task PlaceOrder_WithValidOrderParams_ShouldSucceed()
        {
            //TODO : test when market is open
            if (!MarketDataUtils.IsMarketOpen())
                Assert.Ignore();

            // Setup
            var dummy = MakeDummyContract("GME");
            var details = await _broker.GetContractDetailsAsync(dummy);
            var contract = details.First().Contract;

            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = 5, LmtPrice = 5 };
            order.Id = await _broker.GetNextValidOrderIdAsync();

            // Test
            var orderMessage = await _broker.PlaceOrderAsync(contract, order);

            // Assert
            Assert.NotNull(orderMessage);
            Assert.NotNull(orderMessage.OrderStatus);

            // TODO : verify this again
            Assert.IsTrue(orderMessage.OrderStatus.Status == Status.PreSubmitted || orderMessage.OrderStatus.Status == Status.Submitted);
        }

        [Test]
        public async Task PlaceOrder_WithMarketOrderFilledInstantly_ShouldSucceed()
        {
            //TODO : test when market is open
            if (!MarketDataUtils.IsMarketOpen())
                Assert.Ignore();

            // Setup
            var contract = await GetContract("GME");
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = 5, LmtPrice = 5 };
            order.Id = await _broker.GetNextValidOrderIdAsync();

            // Test
            var orderMessage = await _broker.PlaceOrderAsync(contract, order);

            // Assert
            Assert.NotNull(orderMessage);
            Assert.Null(orderMessage.OrderStatus);
            Assert.NotNull(orderMessage.OrderExecution);
            Assert.NotNull(orderMessage.CommissionInfo);
        }

        [Test]
        public async Task PlaceOrder_WithLimitPriceTooFarFromCurrentPrice_ShouldBeCancelled()
        {
            //TODO : test when market is open
            if (!MarketDataUtils.IsMarketOpen())
                Assert.Ignore();

            // Setup
            var contract = await GetContract("GME");
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = 5, LmtPrice = 5 };
            order.Id = await _broker.GetNextValidOrderIdAsync();

            // Test
            var orderMessage = await _broker.PlaceOrderAsync(contract, order);

            // Assert
            Assert.NotNull(orderMessage);
            Assert.NotNull(orderMessage.OrderStatus);
            Assert.IsTrue(orderMessage.OrderStatus.Status == Status.Cancelled);
        }

        [Test]
        public async Task PlaceOrder_WithQuantityTooLarge_ShouldFail()
        {
            //TODO : test when market is open
            if (!MarketDataUtils.IsMarketOpen())
                Assert.Ignore();

            // Setup
            var contract = await GetContract("GME");
            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 500000000000};
            order.Id = await _broker.GetNextValidOrderIdAsync();

            // Test
            Assert.ThrowsAsync<ErrorMessage>(async () => await _broker.PlaceOrderAsync(contract, order));
        }

        [Test]
        public async Task CancelOrder_ShouldSucceed()
        {
            // Setup
            var openOrderMsg = await PlaceDummyOrder();
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.PreSubmitted || openOrderMsg.OrderStatus.Status == Status.Submitted);

            // Test
            OrderStatus orderStatus = await _broker.CancelOrderAsync(openOrderMsg.Order.Id);

            // Assert
            Assert.NotNull(orderStatus);
            Assert.IsTrue(orderStatus.Status == Status.Cancelled);
        }

        Contract MakeDummyContract(string symbol)
        {
            return new Stock()
            {
                Currency = "USD",
                Exchange = "SMART",
                Symbol = symbol,
                SecType = "STK"
            };
        }

        async Task<OrderMessage> PlaceDummyOrder()
        {
            var contract = await GetContract("GME");
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = 5, LmtPrice = 5 };
            order.Id = await _broker.GetNextValidOrderIdAsync();
            return await _broker.PlaceOrderAsync(contract, order);
        }

        async Task<Contract> GetContract(string symbol)
        {
            var dummy = MakeDummyContract(symbol);
            var details = await _broker.GetContractDetailsAsync(dummy);
            return details.First().Contract;
        }
    }
}
