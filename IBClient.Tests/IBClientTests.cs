using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using InteractiveBrokers;
using InteractiveBrokers.Messages;
using InteractiveBrokers.Orders;
using InteractiveBrokers.MarketData;
using InteractiveBrokers.Contracts;

namespace Tests.Broker
{
    [TestFixture]
    internal class IBClientTests
    {
        protected IBClient _client;
        protected ConnectResult _connectMessage;

        [OneTimeSetUp]
        public virtual async Task OneTimeSetUp()
        {
            _client = new IBClient(191919);
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
            await _client.DisconnectAsync();
            await Task.Delay(50);

            // Test
            var msg = await _client.ConnectAsync();

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
            await _client.ConnectAsync();
            
            // Test
            Assert.ThrowsAsync<ErrorMessageException>(async () => await _client.ConnectAsync()); 
        }

        [Test]
        public async Task ConnectAsync_CanBeCancelled()
        {
            // Setup
            await _client.DisconnectAsync();
            await Task.Delay(50);

            // Test
            Assert.ThrowsAsync<TimeoutException>(async () => await _client.ConnectAsync(new CancellationToken(true)));
        }

        [Test]
        public async Task RequestAccountUpdates_ReceivesAccount()
        {
            // Setup
            string accountReceived = null;
            var tcs = new TaskCompletionSource<string>();
            var callback = new Action<string, string, string, string>( (key, value, currency, acc) => tcs.SetResult(acc));
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
            if (!Utils.IsMarketOpen())
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
        public async Task PlaceOrder_WithMarketOrderFilledInstantly_ShouldSucceed()
        {
            if (!Utils.IsMarketOpen())
                Assert.Ignore();

            // Setup
            var contract = await GetContractAsync("GME");
            var order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = RandomQty };
            order.Id = await _client.GetNextValidOrderIdAsync();

            // Test
            var result = await _client.PlaceOrderAsync(contract, order);

            // Assert
            if(result is OrderPlacedResult opr)
            {
                Assert.NotNull(opr);
                Assert.NotNull(opr.OrderState);
                Assert.NotNull(opr.OrderStatus);
            }
            else if(result is OrderExecutedResult oer)
            {
                Assert.NotNull(oer);
                Assert.NotNull(oer.OrderExecution);
                Assert.NotNull(oer.CommissionInfo);
            }
            else
                Assert.Fail();
        }

        [Test]
        public async Task PlaceBuyOrder_WhenNotEnoughFunds_ShouldFail()
        {
            if (!Utils.IsMarketOpen())
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
        [Ignore("Keeping this until I move it in Trader tests.")]
        // TODO : enforce disallowing of stock shorting in Trader
        public async Task PlaceSellOrder_WhenNotEnoughPosition_ShouldFail()
        {
            if (!Utils.IsMarketOpen())
                Assert.Ignore();

            // Setup
            var contract = await GetContractAsync("GME");
            
            var buyOrder = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5 };
            var buyOrderResult = await PlaceDummyOrderAsync(buyOrder);
            Assert.IsTrue(buyOrderResult?.OrderState.Status == Status.PreSubmitted || buyOrderResult?.OrderState.Status == Status.Submitted);

            var sellOrder = new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = 10 };
            sellOrder.Id = await _client.GetNextValidOrderIdAsync();

            // Test
            Exception ex = null;
            OrderResult sellOrderResult = null;
            try
            {
                sellOrderResult = await _client.PlaceOrderAsync(contract, sellOrder);
            }
            catch (Exception e)
            {
                ex = e;
            }
            finally
            {
                var r = sellOrderResult as OrderPlacedResult;
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
