using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NUnit.Framework;
using TradingBot.Broker;
using TradingBot.Broker.Client;
using TradingBot.Broker.Client.Messages;
using TradingBot.Broker.Orders;

namespace Tests.Client
{
    [TestFixture]
    internal class IBClientTests
    {
        const int DefaultPort = 7496;
        const string DefaultIP = "127.0.0.1";
        const int DefaultClientId = 191919;

        IBClient _client;
        ILogger _logger;
        ConnectMessage _connectMessage;
        

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _logger = LogManager.GetLogger($"{nameof(IBClientTests)}");
            _client = new IBClient(_logger);
        }

        [SetUp]
        public async Task SetUp()
        {
            _connectMessage = await _client.ConnectAsync(DefaultIP, DefaultPort, DefaultClientId);
            Assert.IsTrue(_connectMessage.AccountCode == "DU5962304");
        }

        [TearDown]
        public async Task TearDown()
        {
            await _client.DisconnectAsync();
            await Task.Delay(50);
        }

        [Test]
        public async Task ConnectAsync_OnSuccess_ReturnsRelevantConnectionInfo()
        {
            // Setup
            await _client.DisconnectAsync();
            await Task.Delay(50);

            // Test
            var msg = await _client.ConnectAsync(DefaultIP, DefaultPort, DefaultClientId);

            // Assert
            Assert.IsNotNull(msg);
            Assert.IsTrue(msg.NextValidOrderId > 0);
            Assert.IsNotNull(msg.AccountCode);
        }

        [Test]
        public async Task ConnectAsync_AlreadyConnected_ThrowsError()
        {
            // Setup
            await _client.DisconnectAsync();
            await Task.Delay(50);
            await _client.ConnectAsync(DefaultIP, DefaultPort, DefaultClientId);
            
            // Test
            Assert.ThrowsAsync<ErrorMessage>(async () => await _client.ConnectAsync(DefaultIP, DefaultPort, DefaultClientId)); 
        }

        [Test]
        public async Task ConnectAsync_CanBeCancelled()
        {
            // Setup
            await _client.DisconnectAsync();
            await Task.Delay(50);

            // Test
            var source = new CancellationTokenSource(5);
            Assert.ThrowsAsync<TaskCanceledException>(async () => await _client.ConnectAsync(DefaultIP, DefaultPort, DefaultClientId, source.Token));
        }

        [Test]
        public async Task RequestAccountUpdates_ReceivesAccount()
        {
            // Setup
            string accountReceived = null;
            var tcs = new TaskCompletionSource<string>();
            var callback = new Action<string>(acc => tcs.SetResult(acc));
            _client.Callbacks.AccountDownloadEnd += callback;

            // Test
            try
            {
                _client.RequestAccountUpdates(_connectMessage.AccountCode);
                accountReceived = await tcs.Task;
            }
            finally
            {
                _client.CancelAccountUpdates(_connectMessage.AccountCode);
                _client.Callbacks.AccountDownloadEnd -= callback;
            }

            // Assert
            Assert.AreEqual(_connectMessage.AccountCode, accountReceived);
        }

        [Test]
        public async Task GetNextValidIdAsync_ReturnsId()
        {
            // Test
            var id = await _client.GetNextValidOrderIdAsync();
            Assert.IsTrue(id > 0);
        }

        [Test]
        public async Task GetContractDetailsAsync_WithValidInput_ReturnsContractDetails()
        {
            // Setup
            var dummy = MakeDummyContract("GME");

            // Test
            var details = await _client.GetContractDetailsAsync(1, dummy);

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
        public Task GetContractDetailsAsync_WithInvalidInput_Throws()
        {
            // Setup
            var dummy = MakeDummyContract("GMEdasdafafsafaf");

            // Test
            Assert.ThrowsAsync<ErrorMessage>(async () => await _client.GetContractDetailsAsync(1, dummy));
            return Task.CompletedTask;
        }

        [Test]
        public async Task PlaceOrder()
        {
            // Setup
            var contract = await GetContract("GME");
            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5 };
            order.Id = await _client.GetNextValidOrderIdAsync();

            // Test
            var msg = await _client.PlaceOrderAsync(contract, order);

            // Assert
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

        async Task<Contract> GetContract(string symbol)
        {
            var dummy = MakeDummyContract(symbol);
            var details = await _client.GetContractDetailsAsync(1, dummy);
            return details.First().Contract;
        }
    }
}
