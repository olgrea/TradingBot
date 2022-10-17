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
        Random _random;

        [OneTimeSetUp]
        public virtual async Task OneTimeSetUp()
        {
            _random = new Random();
            _broker = new IBBroker(191919);
            await Task.CompletedTask;
        }

        [SetUp]
        public virtual async Task SetUp()
        {
            _connectMessage = await _broker.ConnectAsync();
            await Task.Delay(50);
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
            Assert.ThrowsAsync<TimeoutException>(async () => await _broker.ConnectAsync(5));
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

            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = RandomQty, LmtPrice = 5 };
            order.Id = await _broker.GetNextValidOrderIdAsync();

            // Test
            OrderMessage orderMessage = await _broker.PlaceOrderAsync(contract, order);

            // Assert
            var orderPlacedMessage = orderMessage as OrderPlacedMessage;
            Assert.NotNull(orderPlacedMessage);
            Assert.NotNull(orderPlacedMessage.OrderStatus);

            // TODO : verify this again
            Assert.IsTrue(orderPlacedMessage.OrderStatus.Status == Status.PreSubmitted || orderPlacedMessage.OrderStatus.Status == Status.Submitted);
        }

        [Test]
        public async Task PlaceOrder_WithMarketOrderFilledInstantly_ShouldSucceed()
        {
            //TODO : test when market is open
            if (!MarketDataUtils.IsMarketOpen())
                Assert.Ignore();

            // Setup
            var contract = await GetContract("GME");
            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = RandomQty };
            order.Id = await _broker.GetNextValidOrderIdAsync();

            // Test
            var orderMessage = await _broker.PlaceOrderAsync(contract, order);

            // Assert
            if(orderMessage is OrderPlacedMessage orderPlacedMessage)
            {
                Assert.NotNull(orderPlacedMessage);
                Assert.NotNull(orderPlacedMessage.OrderState);
                Assert.NotNull(orderPlacedMessage.OrderStatus);
            }
            else if(orderMessage is OrderExecutedMessage orderExecutedMessage)
            {
                Assert.NotNull(orderExecutedMessage);
                Assert.NotNull(orderExecutedMessage.OrderExecution);
                Assert.NotNull(orderExecutedMessage.CommissionInfo);
            }
            else
                Assert.Fail();
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

        async Task<OrderPlacedMessage> PlaceDummyOrder()
        {
            var contract = await GetContract("GME");
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = RandomQty, LmtPrice = 5 };
            order.Id = await _broker.GetNextValidOrderIdAsync();
            var msg = await _broker.PlaceOrderAsync(contract, order);
            return msg as OrderPlacedMessage;
        }

        int RandomQty => _random.Next(3, 10);

        async Task<Contract> GetContract(string symbol)
        {
            var dummy = MakeDummyContract(symbol);
            var details = await _broker.GetContractDetailsAsync(dummy);
            return details.First().Contract;
        }
    }
}
