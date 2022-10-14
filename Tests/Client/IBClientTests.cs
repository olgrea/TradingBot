using System;
using System.Collections.Generic;
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

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _logger = LogManager.GetLogger($"{nameof(IBClientTests)}");
            _client = new IBClient(_logger);
        }

        [SetUp]
        public async Task SetUp()
        {
            var msg = await _client.ConnectAsync(DefaultIP, DefaultPort, DefaultClientId);
            Assert.IsTrue(msg.AccountCode == "DU5962304");
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
        public async Task GetNextValidIdAsync_ReturnsId()
        {
            // Test
            var id = await _client.GetNextValidOrderIdAsync();
            Assert.IsTrue(id > 0);
        }

        [Test]
        public async Task GetContractsAsync_ReturnsContract()
        {
            // Setup
            var dummy = new Stock()
            {
                Currency = "USD",
                Exchange = "SMART",
                Symbol = "GME",
                SecType = "STK"
            };

            // Test
            var contracts = await _client.GetContractsAsync(1, dummy);

            // Assert
            Assert.NotNull(contracts);
            Assert.IsNotEmpty(contracts);
            Assert.IsTrue(contracts.First().Id > 0);
        }

        [Test]
        public Task GetContractsAsync_WithInvalidData_Throws()
        {
            // Setup
            var dummy = new Stock()
            {
                Currency = "USD",
                Exchange = "SMART",
                Symbol = "GMEdasdafafsafaf",
                SecType = "STK"
            };

            // Test
            Assert.ThrowsAsync<ErrorMessage>(async () => await _client.GetContractsAsync(1, dummy));
            return Task.CompletedTask;
        }

        [Test]
        public async Task PlaceOrder()
        {
            // Setup
            //var order = new Order()

            // Test
            var id = await _client.GetNextValidOrderIdAsync();
            Assert.IsTrue(id > 0);
        }
    }
}
