using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using InteractiveBrokers.Messages;
using InteractiveBrokers.Orders;
using InteractiveBrokers.MarketData;
using InteractiveBrokers.Contracts;
using InteractiveBrokers.Accounts;

namespace IBClient.Tests
{
    [TestFixture]
    public class IBClientTests
    {
        protected InteractiveBrokers.IBClient _client;
        protected ConnectResult _connectMessage;

        [OneTimeSetUp]
        public virtual async Task OneTimeSetUp()
        {
            _client = new InteractiveBrokers.IBClient(191919);
            await Task.CompletedTask;
        }

        [SetUp]
        public virtual async Task SetUp()
        {
            _connectMessage = await _client.ConnectAsync();
            await Task.Delay(50);
            Assert.IsTrue(_connectMessage.AccountCode == "DU5962304");
        }

        [TearDown]
        public async Task TearDown()
        {
            _client.CancelAllOrders();
            await Task.Delay(50);
            await _client.DisconnectAsync();
            await Task.Delay(50);
        }

        [Test]
        public async Task ConnectAsync_OnSuccess_ReturnsRelevantConnectionInfo()
        {
            // Setup
            var client = new InteractiveBrokers.IBClient();
            try
            {
                // Test
                var result = await client.ConnectAsync();

                // Assert
                Assert.IsNotNull(result);
                Assert.IsTrue(result.NextValidOrderId > 0);
                Assert.IsNotNull(result.AccountCode);
            }
            finally
            {
                await client.DisconnectAsync();
            }
        }

        [Test]
        public async Task ConnectAsync_AlreadyConnected_ThrowsError()
        {
            // Setup
            var client = new InteractiveBrokers.IBClient();
            try
            {
                await client.ConnectAsync();
                
                // Test and Assert
                Assert.ThrowsAsync<ErrorMessageException>(async () => await client.ConnectAsync()); 
            }
            finally
            {
                await client.DisconnectAsync();
            }
        }

        [Test]
        public async Task ConnectAsync_CanBeCancelled()
        {
            // Setup
            var client = new InteractiveBrokers.IBClient();
            try
            {
                // Test and Assert
                Assert.ThrowsAsync<TaskCanceledException>(async () => await client.ConnectAsync(new CancellationToken(true)));
            }
            finally
            {
                await client.DisconnectAsync();
            }
        }

        [Test]
        public async Task ConnectAsync_TimesOutAfterAWhile()
        {
            // Setup
            var client = new InteractiveBrokers.IBClient();
            try
            {
                // Test and Assert
                Assert.ThrowsAsync<TimeoutException>(async () => await client.ConnectAsync(new TimeSpan(1), CancellationToken.None));
            }
            finally
            {
                await client.DisconnectAsync();
            }
        }

        [Test]
        public async Task GetAccountAsync_WithValidAccountCode_GetsTheAccount()
        {
            Account account = await _client.GetAccountAsync(_connectMessage.AccountCode);
            Assert.IsNotNull(account);
            Assert.AreEqual(_connectMessage.AccountCode, account.Code);
            Assert.AreNotEqual(account.Time, default(DateTime));
            Assert.True(account.CashBalances.Any());
        }

        [Test]
        public async Task GetAccountAsync_NoAccountCodeProvided_SingleAccountStructure_GetsTheDefaultAccount()
        {
            Account account = await _client.GetAccountAsync();
            Assert.IsNotNull(account);
            Assert.AreEqual(_connectMessage.AccountCode, account.Code);
            Assert.AreNotEqual(account.Time, default(DateTime));
            Assert.True(account.CashBalances.Any());
        }

        [Test]
        public async Task GetAccountAsync_WithInvalidAccountCode_SingleAccountStructure_IgnoresItAndGetsTheDefaultAccount()
        {
            Account account = await _client.GetAccountAsync("INVALID_ACCOUNT");
            Assert.IsNotNull(account);
            Assert.AreEqual(_connectMessage.AccountCode, account.Code);
            Assert.AreNotEqual(account.Time, default(DateTime));
            Assert.True(account.CashBalances.Any());
        }

        [Test]
        public async Task RequestAccountUpdates_ReceivesAccount()
        {
            // Setup
            string accountReceived = null;
            var tcs = new TaskCompletionSource<string>();
            var callback = new Action<AccountValue>( accVal => tcs.SetResult(accVal.AccountName));
            _client.AccountValueUpdated += callback;

            // Test
            try
            {
                _client.RequestAccountUpdates(_connectMessage.AccountCode);
                accountReceived = await tcs.Task;
            }
            finally
            {
                _client.CancelAccountUpdates(_connectMessage.AccountCode);
                _client.AccountValueUpdated -= callback;
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
            var details = await _client.GetContractDetailsAsync(dummy);

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
            Assert.ThrowsAsync<ErrorMessageException>(async () => await _client.GetContractDetailsAsync(dummy));
            await Task.CompletedTask;
        }

        [Test]
        public async Task PlaceOrder_WithOrderIdNotSet_Throws()
        {
            // Setup
            var contract = await GetContractAsync("GME");
            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5 };

            // Test
            Assert.ThrowsAsync<ArgumentException>(async () => await _client.PlaceOrderAsync(contract, order));
        }

        [Test]
        public async Task PlaceOrder_WithValidOrderParams_ShouldSucceed()
        {
            if (!MarketDataUtils.IsMarketOpen())
                Assert.Ignore();

            // Setup
            var dummy = MakeDummyContract("GME");
            var details = await _client.GetContractDetailsAsync(dummy);
            var contract = details.First().Contract;

            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = RandomQty, LmtPrice = 5 };
            order.Id = await _client.GetNextValidOrderIdAsync();

            // Test
            OrderResult result = await _client.PlaceOrderAsync(contract, order);

            // Assert
            var orderPlacedResult = result as OrderPlacedResult;
            Assert.NotNull(orderPlacedResult);
            Assert.NotNull(orderPlacedResult.OrderStatus);

            Assert.IsTrue(orderPlacedResult.OrderStatus.Status == Status.PreSubmitted || orderPlacedResult.OrderStatus.Status == Status.Submitted);
        }

        [Test]
        public async Task PlaceBuyOrder_WhenNotEnoughFunds_ShouldFail()
        {
            if (!MarketDataUtils.IsMarketOpen())
                Assert.Ignore();

            // Setup
            var contract = await GetContractAsync("GME");
            var account = await _client.GetAccountAsync(_connectMessage.AccountCode);
            var bidAsk = await _client.GetLatestBidAskAsync(contract);
            var qty = (int)Math.Round(account.CashBalances["BASE"] / bidAsk.Ask + 500);

            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = qty};
            order.Id = await _client.GetNextValidOrderIdAsync();

            // Test
            // TODO : for some reason I'm receiving expected error 201 ONLY when out of Assert.ThrowsAsync() ?? related to ConfigureAwait() maybe ?
            //Assert.ThrowsAsync<ErrorMessageException>(async () => await _broker.PlaceOrderAsync(contract, order));
            Exception ex = null;
            OrderResult result = null;
            try
            {
                result = await _client.PlaceOrderAsync(contract, order);
            }
            catch (Exception e)
            {
                ex = e;
            }
            finally
            {
                var r = result as OrderPlacedResult;
                Assert.IsNull(r);
                Assert.IsInstanceOf<ErrorMessageException>(ex);
            }
        }

        [Test]
        public async Task CancelOrder_ShouldSucceed()
        {
            // Setup
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = RandomQty, LmtPrice = 5 };
            var openOrderMsg = await PlaceDummyOrderAsync(order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.PreSubmitted || openOrderMsg.OrderStatus.Status == Status.Submitted);

            // Test
            OrderStatus orderStatus = await _client.CancelOrderAsync(openOrderMsg.Order.Id);

            // Assert
            Assert.NotNull(orderStatus);
            Assert.IsTrue(orderStatus.Status == Status.Cancelled);
        }

        [Test]
        public async Task CancelOrder_AlreadyCanceled_Throws()
        {
            // Setup
            var order = new LimitOrder() { Action = OrderAction.BUY, TotalQuantity = RandomQty, LmtPrice = 5 };
            var openOrderMsg = await PlaceDummyOrderAsync(order);
            Assert.NotNull(openOrderMsg);
            Assert.NotNull(openOrderMsg.OrderStatus);
            Assert.IsTrue(openOrderMsg.OrderStatus.Status == Status.PreSubmitted || openOrderMsg.OrderStatus.Status == Status.Submitted);

            // Test
            OrderStatus orderStatus = await _client.CancelOrderAsync(openOrderMsg.Order.Id);
            Assert.NotNull(orderStatus);
            Assert.IsTrue(orderStatus.Status == Status.Cancelled);

            // Assert
            //Assert.ThrowsAsync<ErrorMessageException>(async () => await _broker.CancelOrderAsync(openOrderMsg.Order.Id));

            Exception ex = null;
            OrderStatus os2 = null;
            try
            {
                os2 = await _client.CancelOrderAsync(openOrderMsg.Order.Id);
            }
            catch (Exception e)
            {
                ex = e;
            }
            finally
            {
                Assert.IsInstanceOf<ErrorMessageException>(ex);
            }
        }

        [Test]
        public async Task GetCurrentTime_TimeIncrements()
        {
            var t1 = await _client.GetCurrentTimeAsync();
            await Task.Delay(2000);
            var t2 = await _client.GetCurrentTimeAsync();
            Assert.Greater(t2, t1);
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

        async Task<OrderPlacedResult> PlaceDummyOrderAsync(Order order)
        {
            var contract = await GetContractAsync("GME");
            order.Id = await _client.GetNextValidOrderIdAsync();
            var result = await _client.PlaceOrderAsync(contract, order);
            return result as OrderPlacedResult;
        }

        int RandomQty => new Random().Next(3, 10);

        async Task<Contract> GetContractAsync(string symbol)
        {
            var dummy = MakeDummyContract(symbol);
            var details = await _client.GetContractDetailsAsync(dummy);
            return details.First().Contract;
        }
    }
}
