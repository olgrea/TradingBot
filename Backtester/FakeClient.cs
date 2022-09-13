using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Broker;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Utils;

namespace Backtester
{
    internal class FakeClient : IIBClient
    {
        static class TimeDelays
        {
            public static double TimeFactor = 1;
            // All in ms
            public static int AccountUpdateInterval => (int)(3 * 60 * 1000 * TimeFactor);
        }
        DateTime _currentTime;


        Account _fakeAccount;
        Task _accountUpdateTask;
        CancellationTokenSource _accountUpdateCancellation;
        CancellationTokenSource _accountUpdateDelayCancellation;

        HashSet<Order> _openOrders;
        HashSet<Order> _executedOrders;
        Bar _latestBar;

        ILogger _logger;

        public FakeClient(DateTime startTime, ILogger logger)
        {
            _currentTime = startTime;

            Callbacks = new IBCallbacks(logger);
            _logger = logger;
            _fakeAccount = new Account()
            {
                Code = "FAKEACCOUNT123",
                CashBalances = new Dictionary<string, double>()
                {
                     {"USD", 5000}
                }
            };
        }

        public IBCallbacks Callbacks { get; }

        public void FeedHistoricalData(Bar bar)
        {
            _latestBar = bar;
        }

        public void CancelAllOrders()
        {
            throw new NotImplementedException();
        }

        public void CancelFiveSecondsBarsRequest(int reqId)
        {
            throw new NotImplementedException();
        }

        public void CancelOrder(int orderId)
        {
            throw new NotImplementedException();
        }

        public void CancelPnL(int contractId)
        {
            throw new NotImplementedException();
        }

        public void CancelPositions()
        {
            throw new NotImplementedException();
        }

        public void CancelTickByTickData(int reqId)
        {
            throw new NotImplementedException();
        }

        public void Connect(string host, int port, int clientId)
        {
            throw new NotImplementedException();
        }

        public void Disconnect()
        {
            throw new NotImplementedException();
        }

        public void PlaceOrder(Contract contract, Order order)
        {
            throw new NotImplementedException();
        }

        public void RequestAccount(string accountCode, bool receiveUpdates = true)
        {
            SendAccountUpdate(accountCode);
            if (receiveUpdates)
            {
                StartAccountUpdateTask(accountCode);
            }
            else
            {
                _accountUpdateCancellation.Cancel();
                _accountUpdateCancellation.Dispose();
                _accountUpdateDelayCancellation.Cancel();
                _accountUpdateDelayCancellation.Dispose();
                _accountUpdateCancellation = null;
                _accountUpdateDelayCancellation = null;
                _accountUpdateTask = null;
            }
        }

        void StartAccountUpdateTask(string accountCode)
        {
            _accountUpdateCancellation = new CancellationTokenSource();
            var mainToken = _accountUpdateCancellation.Token;
            _accountUpdateTask = Task.Factory.StartNew(() =>
            {
                _accountUpdateDelayCancellation = CancellationTokenSource.CreateLinkedTokenSource(mainToken, CancellationToken.None);
                var delayToken = _accountUpdateDelayCancellation.Token;

                while (!mainToken.IsCancellationRequested)
                {
                    try
                    {
                        Task.Delay(TimeDelays.AccountUpdateInterval, delayToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _accountUpdateDelayCancellation.Dispose();
                        _accountUpdateDelayCancellation = CancellationTokenSource.CreateLinkedTokenSource(mainToken, CancellationToken.None);
                        delayToken = _accountUpdateDelayCancellation.Token;
                    }
                    SendAccountUpdate(accountCode);
                }

            }, mainToken);
        }

        void ForceAccountUpdate() => _accountUpdateDelayCancellation.Cancel();

        void SendAccountUpdate(string accountCode)
        {
            Callbacks.updateAccountTime(_currentTime.ToString()); //TODO : make sure format is correct
            Callbacks.updateAccountValue("CashBalance", _fakeAccount.CashBalances.First().Value.ToString(), "USD", _fakeAccount.Code);
            Callbacks.accountDownloadEnd(accountCode);
        }

        public void RequestContract(int reqId, Contract contract)
        {
            throw new NotImplementedException();
        }

        public void RequestFiveSecondsBars(int reqId, Contract contract)
        {
            throw new NotImplementedException();
        }

        public void RequestHistoricalData(int reqId, Contract contract, string endDateTime, string durationStr, string barSizeStr, bool onlyRTH)
        {
            throw new NotImplementedException();
        }

        public void RequestOpenOrders()
        {
            throw new NotImplementedException();
        }

        public void RequestPnL(int reqId, string accountCode, int contractId)
        {
            throw new NotImplementedException();
        }

        public void RequestPositions()
        {
            throw new NotImplementedException();
        }

        public void RequestTickByTickData(int reqId, Contract contract, string tickType)
        {
            throw new NotImplementedException();
        }

        public void RequestValidOrderIds()
        {
            throw new NotImplementedException();
        }
    }
}
