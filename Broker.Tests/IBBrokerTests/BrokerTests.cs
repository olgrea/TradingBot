using Broker;
using Broker.Accounts;
using Broker.IBKR.Client;
using Broker.Orders;
using Broker.Utils;
using NLog;
using NUnit.Framework;
using Broker.Tests;

namespace IBBrokerTests
{
    [TestFixture]
    public class BrokerTests
    {
        protected string _accountCode;
        protected IBroker _broker;
        protected ILogger _logger;

        [SetUp]
        public virtual async Task SetUp()
        {
            _logger = TestsUtils.CreateLogger();
            _broker = TestsUtils.CreateBroker(_logger);
            _accountCode = await _broker.ConnectAsync();
            _logger?.PrintCurrentTestName();
        }

        [TearDown]
        public virtual async Task TearDown()
        {
            await Task.Delay(50);
            await _broker.DisconnectAsync();
            await Task.Delay(50);
        }

        [Test]
        public void ConnectAsync_AlreadyConnected_ThrowsError()
        {
            Assert.ThrowsAsync<ErrorMessageException>(async () => await _broker.ConnectAsync());
        }

        [Test]
        public async Task GetAccountAsync_WithValidAccountCode_GetsTheAccount()
        {
            Account account = await _broker.GetAccountAsync();
            Assert.IsNotNull(account);
            Assert.AreEqual(_accountCode, account.Code);
            Assert.AreNotEqual(account.Time, default(DateTime));
        }

        [Test]
        public async Task GetAccountAsync_ReceivesPositionsUpdates()
        {
            TestsUtils.Assert.MarketIsOpen();

            string ticker = "GME";
            var randomQty = new Random().Next(2, 10);
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            
            Account account = await _broker.GetAccountAsync();
            double currentPos = 0;
            if (account.Positions.TryGetValue(ticker, out Position pos))
                currentPos = pos.PositionAmount;

            Position? positionAfterOrderExec = null;
            var positionUpdate = new Action<Position>(p => 
            {
                if(p.Ticker == ticker)
                {
                    positionAfterOrderExec = p;
                    tcs.TrySetResult();
                }
            });
            var error = new Action<Exception>(e => tcs.TrySetException(e));

            _broker.PositionUpdated += positionUpdate;
            _broker.ErrorOccured += error;

            var buyOrder = new MarketOrder() {Action = OrderAction.BUY, TotalQuantity = randomQty };
            await _broker.OrderManager.PlaceOrderAsync(ticker, buyOrder);
            await _broker.OrderManager.AwaitExecutionAsync(buyOrder);
            await tcs.Task;

            Assert.NotNull(positionAfterOrderExec);
            Assert.AreEqual(currentPos + randomQty, positionAfterOrderExec.PositionAmount);
        }

        [Test]
        public async Task GetAccountAsync_ReceivesPnLUpdates()
        {
            TestsUtils.Assert.MarketIsOpen();

            string ticker = "GME";
            var randomQty = new Random().Next(2, 10);
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            await _broker.OrderManager.SellAllPositionsAsync();
            Account account = await _broker.GetAccountAsync();

            int receiveUntil = 5;
            int nbPnLReceived = 0;
            var pnlUpdates = new Action<PnL>(pnl =>
            {
                if (pnl.Ticker == ticker)
                {
                    if (nbPnLReceived < receiveUntil)
                        nbPnLReceived++;

                    if(nbPnLReceived == receiveUntil)
                        tcs.TrySetResult();
                }
            });
            var error = new Action<Exception>(e => tcs.TrySetException(e));

            _broker.PnLUpdated += pnlUpdates;
            _broker.ErrorOccured += error;

            var buyOrder = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = randomQty };
            await _broker.OrderManager.PlaceOrderAsync(ticker, buyOrder);
            await _broker.OrderManager.AwaitExecutionAsync(buyOrder);
            await tcs.Task;

            Assert.AreEqual(receiveUntil, nbPnLReceived);
        }

        // TWS account updates are weirdly implemented. See IBClient.RequestAccountUpdates.
        [Test, Explicit]
        public async Task RequestAccountUpdates_NoChangeInPositions_ReceivesThemAtThreeMinutesInterval()
        {
            TestsUtils.Assert.MarketIsOpen();

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            //var timeout = TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 3 * 60 * 1000 + 500);
            var timeout = TimeSpan.FromMilliseconds(3 * 60 * 1000 + 500);
            var times = new List<DateTime>();

            DateTime timeAtRequest = default;
            Action<AccountValue> accValueUpdated = val =>
            {
                if(val.Key == AccountValueKey.Time)
                {
                    DateTime time = DateTime.Parse(val.Value);
                    times.Add(time);
                    _logger?.Debug($"received : {time}");
                }
            };
            var error = new Action<Exception>(e => tcs.TrySetException(e));

            _broker.AccountValueUpdated += accValueUpdated;
            _broker.ErrorOccured += error;
            try
            {
                timeAtRequest = await _broker.GetServerTimeAsync();
                _broker.RequestAccountUpdates();
                await Task.Delay(timeout);
                tcs.TrySetResult();
            }
            catch(Exception e)
            {
                tcs.TrySetException(e);
            }
            finally
            {
                _broker.AccountValueUpdated -= accValueUpdated;
                _broker.ErrorOccured -= error;
                _broker.CancelAccountUpdates();
                await _broker.OrderManager.CancelAllOrdersAsync();
                await _broker.OrderManager.SellAllPositionsAsync();
            }

            Assert.IsTrue(tcs.Task.IsCompletedSuccessfully);

            var lower = timeAtRequest.Floor(TimeSpan.FromMinutes(3));
            var higher = timeAtRequest.Ceiling(TimeSpan.FromMinutes(3));
            Assert.Multiple(() =>
            {
                foreach (var time in times)
                {
                    Assert.LessOrEqual(lower, time);
                    Assert.GreaterOrEqual(higher, time);
                }
            });
        }

        // TWS account updates are weirdly implemented. See IBClient.RequestAccountUpdates.
        [Test, Explicit]
        public async Task RequestAccountUpdates_ChangeInPosition_ReceivesThemOnPositionChange()
        {
            TestsUtils.Assert.MarketIsOpen();
            
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var times = new List<DateTime>();

            Action<AccountValue> accValueUpdated = val =>
            {
                if (val.Key == AccountValueKey.Time)
                {
                    DateTime time = DateTime.Parse(val.Value);
                    times.Add(time);
                    _logger?.Debug($"received : {time}");
                }
            };
            var error = new Action<Exception>(e =>
            {
                tcs.TrySetException(e);
            });

            DateTime timeAtRequest = default;
            OrderExecutedResult? execResult = null;

            _broker.AccountValueUpdated += accValueUpdated;
            _broker.ErrorOccured += error;
            try
            {
                timeAtRequest = await _broker.GetServerTimeAsync();
                _broker.RequestAccountUpdates();
                
                MarketOrder order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5 };
                await _broker.OrderManager.PlaceOrderAsync("GME", order);
                execResult = await _broker.OrderManager.AwaitExecutionAsync(order);
                await Task.Delay(5*1000);
                tcs.TrySetResult();
            }
            catch (Exception e)
            {
                tcs.TrySetException(e);
            }
            finally
            {
                _broker.AccountValueUpdated -= accValueUpdated;
                _broker.ErrorOccured -= error;
                _broker.CancelAccountUpdates();
                await _broker.OrderManager.CancelAllOrdersAsync();
                await _broker.OrderManager.SellAllPositionsAsync();
            }

            Assert.True(tcs.Task.IsCompletedSuccessfully);
            Assert.NotNull(execResult);

            var lower = timeAtRequest.Floor(TimeSpan.FromMinutes(3));
            var higher = execResult.Time.AddSeconds(5);
            Assert.Multiple(() =>
            {
                foreach (var time in times)
                {
                    Assert.LessOrEqual(lower, time);
                    Assert.GreaterOrEqual(higher, time);
                }
            });
        }

        [Test]
        public async Task GetServerTime_ReceivesIt()
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var times = new List<DateTime>();
            for (int i = 0; i < 5; i++)
                times.Add(await _broker.GetServerTimeAsync());

            Assert.AreEqual(5, times.Count);  
        }
    }
}
