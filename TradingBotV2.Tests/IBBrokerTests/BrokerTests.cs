using System.Diagnostics;
using NLog;
using NUnit.Framework;
using TradingBotV2.Broker;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.Orders;
using TradingBotV2.IBKR.Client;
using TradingBotV2.Tests;

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
            Assert.ThrowsAsync<ErrorMessage>(async () => await _broker.ConnectAsync());
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

        // TODO : need tests for account, positions and PnL updates

        [Test]
        [Ignore("TWS account updates are not implemented correctly. See IBClient.RequestAccountUpdates.")]
        public async Task RequestAccountUpdates_NoChangeInPositions_ReceivesThemAtThreeMinutesInterval()
        {
            TestsUtils.Assert.MarketIsOpen();

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(Debugger.IsAttached ? -1 : 3 * 60 * 1000 + 500);
            cancellation.Token.Register(() => tcs.TrySetException(new TimeoutException()));

            DateTime? updateReceivedInstantly = null;
            DateTime? updateReceivedAfter3Mins = null;

            Action<AccountValue> accValueUpdated = val =>
            {
                if(val.Key == AccountValueKey.Time)
                {
                    if (updateReceivedInstantly == null)
                        updateReceivedInstantly = DateTime.Parse(val.Value);
                    else
                    {
                        updateReceivedAfter3Mins = DateTime.Parse(val.Value);
                        tcs.TrySetResult();
                    }
                }
            };
            var error = new Action<Exception>(e => tcs.TrySetException(e));

            _broker.AccountValueUpdated += accValueUpdated;
            _broker.ErrorOccured += error;
            try
            {
                _broker.RequestAccountUpdates();
                await tcs.Task;
            }
            finally
            {
                _broker.AccountValueUpdated -= accValueUpdated;
                _broker.ErrorOccured -= error;
                _broker.CancelAccountUpdates();
            }

            Assert.IsNotNull(updateReceivedInstantly);
            Assert.IsNotNull(updateReceivedAfter3Mins);
            Assert.IsTrue(updateReceivedAfter3Mins - updateReceivedInstantly >= TimeSpan.FromMinutes(3));
        }

        [Test]
        public async Task RequestAccountUpdates_ChangeInPosition_ReceivesThemOnPositionChange()
        {
            TestsUtils.Assert.MarketIsOpen();
            
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(Debugger.IsAttached ? -1 : 3 * 60 * 1000 + 500);
            cancellation.Token.Register(() => tcs.TrySetException(new TimeoutException()));

            DateTime? updateReceivedInstantly = null;
            DateTime? updateReceivedAfterPositionChange = null;

            Action<AccountValue> accValueUpdated = val =>
            {
                if (val.Key == AccountValueKey.Time)
                {
                    if (updateReceivedInstantly == null)
                        updateReceivedInstantly = DateTime.Parse(val.Value);
                    else
                    {
                        updateReceivedAfterPositionChange = DateTime.Parse(val.Value);
                        tcs.TrySetResult();
                    }
                }
            };
            var error = new Action<Exception>(e => tcs.TrySetException(e));

            _broker.AccountValueUpdated += accValueUpdated;
            _broker.ErrorOccured += error;
            try
            {
                _broker.RequestAccountUpdates();
                await Task.Delay(500);
                
                MarketOrder order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5 };
                var placedResult = await _broker.OrderManager.PlaceOrderAsync("GME", order);
                await tcs.Task;
            }
            finally
            {
                _broker.AccountValueUpdated -= accValueUpdated;
                _broker.ErrorOccured -= error;
                _broker.CancelAccountUpdates();
            }

            Assert.IsNotNull(updateReceivedInstantly);
            Assert.IsNotNull(updateReceivedAfterPositionChange);
            Assert.IsTrue(updateReceivedAfterPositionChange - updateReceivedInstantly < TimeSpan.FromMinutes(3));
        }
    }
}
