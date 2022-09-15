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
        // Times are in ms
        static class TimeDelays
        {
            public static double TimeScale = 1;
            public static int OneSecond = (int)Math.Round(1 * 1000 * TimeScale);
            public static int AccountUpdateInterval => (int)Math.Round(3 * 60 * 1000 * TimeScale);
        }

        Task _passingTimeTask;
        CancellationTokenSource _passingTimeCancellation;

        Account _fakeAccount;
        Task _accountUpdateTask;
        CancellationTokenSource _accountUpdateCancellation;
        CancellationTokenSource _accountUpdateDelayCancellation;

        //TODO : use IBApi classes?
        HashSet<Order> _openOrders;
        HashSet<Order> _executedOrders;
        
        DateTime _start;
        DateTime _end;
        DateTime _currentFakeTime;
        LinkedList<Bar> _dailyBars;
        LinkedListNode<Bar> _currentBarNode;

        Contract _contract;
        ILogger _logger;
        IBClient _client;

        int _reqId5SecBar = -1;
        bool _enable5SecBar = false;

        public FakeClient(DateTime start, DateTime end, IEnumerable<Bar> dailyBars, IBClient client, ILogger logger)
        {
            _currentFakeTime = start;
            _start = start;
            _end = end; 
            _dailyBars = new LinkedList<Bar>(dailyBars);
            _currentBarNode = _dailyBars.First;
            _client = client;
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

        public IBCallbacks Callbacks => _client.Callbacks;

        public void Start()
        {
            StartPassingTimeTask();
        }

        public void Stop()
        {
            StopPassingTimeTask();
            StopAccountUpdateTask();
        }

        public void Connect(string host, int port, int clientId)
        {
            _client.Connect(host, port, clientId);
        }

        public void Disconnect()
        {
            _client.Disconnect();
        }

        public void PlaceOrder(Contract contract, Order order)
        {
            if (contract != _contract)
                throw new InvalidOperationException();

            //TODO : make sure correct callbacks are called


            // get price at current time

            
            // get actual commission using what if


        }

        public void CancelOrder(int orderId)
        {
            throw new NotImplementedException();
        }

        public void CancelAllOrders() { }

        public void RequestAccount(string accountCode, bool receiveUpdates = true)
        {
            SendAccountUpdate(accountCode);
            if (receiveUpdates)
            {
                StartAccountUpdateTask(accountCode);
            }
            else
            {
                StopAccountUpdateTask();
            }
        }

        public void RequestContract(int reqId, Contract contract)
        {
            if (contract != _contract)
                throw new InvalidOperationException();

            var cd = new IBApi.ContractDetails();
            cd.Contract = _contract.ToIBApiContract();
            Callbacks.contractDetails(reqId, cd);
            Callbacks.contractDetailsEnd(reqId);
        }

        public void RequestFiveSecondsBars(int reqId, Contract contract)
        {
            if (contract != _contract)
                throw new InvalidOperationException();

            _reqId5SecBar = reqId;
            _enable5SecBar = true;
        }

        public void CancelFiveSecondsBarsRequest(int reqId)
        {
            _reqId5SecBar = -1;
            _enable5SecBar = false;
        }

        public void RequestOpenOrders()
        {
            foreach (var o in _openOrders)
                Callbacks.openOrder(o.Id, _contract.ToIBApiContract(), o.ToIBApiOrder(), new IBApi.OrderState() { Status = "Submitted" });
            Callbacks.openOrderEnd();
        }

        public void RequestPnL(int reqId, string accountCode, int contractId)
        {
            throw new NotImplementedException();
        }

        public void CancelPnL(int contractId)
        {
            throw new NotImplementedException();
        }

        public void RequestPositions()
        {
            throw new NotImplementedException();
        }

        public void CancelPositions()
        {
            throw new NotImplementedException();
        }

        public void RequestHistoricalData(int reqId, Contract contract, string endDateTime, string durationStr, string barSizeStr, bool onlyRTH)
        {
            throw new NotImplementedException();
        }

        public void RequestTickByTickData(int reqId, Contract contract, string tickType)
        {
            throw new NotImplementedException();
        }

        public void CancelTickByTickData(int reqId)
        {
            throw new NotImplementedException();
        }

        public void RequestValidOrderIds()
        {
            throw new NotImplementedException();
        }

        void StartPassingTimeTask()
        {
            _passingTimeCancellation = new CancellationTokenSource();
            var mainToken = _passingTimeCancellation.Token;
            _passingTimeTask = Task.Factory.StartNew(() =>
            {
                var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(mainToken, CancellationToken.None);
                var delayToken = delayCancellation.Token;

                while (!mainToken.IsCancellationRequested)
                {
                    try
                    {
                        ClockTick(_currentFakeTime);
                        Task.Delay(TimeDelays.OneSecond, delayToken).Wait();
                        _currentFakeTime = _currentFakeTime.AddSeconds(1);
                    }
                    catch (OperationCanceledException)
                    {
                        delayCancellation.Dispose();
                        if(!mainToken.IsCancellationRequested)
                        {
                            delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(mainToken, CancellationToken.None);
                            delayToken = delayCancellation.Token;
                        }
                    }
                }

            }, mainToken);
        }

        void StopPassingTimeTask()
        {
            _passingTimeCancellation.Cancel();
            _passingTimeCancellation.Dispose();
            _passingTimeCancellation = null;
            _passingTimeTask = null;
        }

        void ClockTick(DateTime newTime)
        {
            if(newTime.Second % 5 == 0)
            {
                _currentBarNode = _currentBarNode.Next;
                if(_enable5SecBar)
                {
                    var b = _currentBarNode.Value;
                    DateTimeOffset dto = new DateTimeOffset(b.Time.ToUniversalTime());
                    Callbacks.realtimeBar(_reqId5SecBar, dto.ToUnixTimeSeconds(), b.Open, b.High, b.Low, b.Close, b.Volume, 0, b.TradeAmount);
                }
            }
        }

        void StopAccountUpdateTask()
        {
            _accountUpdateCancellation.Cancel();
            _accountUpdateCancellation.Dispose();
            _accountUpdateDelayCancellation.Cancel();
            _accountUpdateDelayCancellation.Dispose();
            _accountUpdateCancellation = null;
            _accountUpdateDelayCancellation = null;
            _accountUpdateTask = null;
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
                        Task.Delay(TimeDelays.AccountUpdateInterval, delayToken).Wait();
                    }
                    catch (OperationCanceledException)
                    {
                        _accountUpdateDelayCancellation.Dispose();
                        if(!mainToken.IsCancellationRequested)
                        {
                            _accountUpdateDelayCancellation = CancellationTokenSource.CreateLinkedTokenSource(mainToken, CancellationToken.None);
                            delayToken = _accountUpdateDelayCancellation.Token;
                        }
                    }

                    if(!mainToken.IsCancellationRequested)
                        SendAccountUpdate(accountCode);
                }

            }, mainToken);
        }

        void ForceAccountUpdate() => _accountUpdateDelayCancellation.Cancel();

        void SendAccountUpdate(string accountCode)
        {
            //TODO : insert delay between calls?
            Callbacks.updateAccountTime(_currentFakeTime.ToString()); //TODO : make sure format is correct
            Callbacks.updateAccountValue("CashBalance", _fakeAccount.CashBalances.First().Value.ToString(), "USD", _fakeAccount.Code);
            Callbacks.accountDownloadEnd(accountCode);
        }
    }
}
