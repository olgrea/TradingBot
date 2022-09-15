using System;
using System.Collections.Concurrent;
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
        }

        ConcurrentQueue<Action> _messageQueue;
        Task _consumerTask;
        CancellationTokenSource _consumerTaskCancellation;

        DateTime _start;
        DateTime _end;
        DateTime _currentFakeTime;
        event Action<DateTime> ClockTick;
        Task _passingTimeTask;
        CancellationTokenSource _passingTimeCancellation;

        DateTime _lastAccountUpdate;
        Account _fakeAccount;

        //TODO : use IBApi classes?
        HashSet<Order> _openOrders;
        HashSet<Order> _executedOrders;

        LinkedList<Bar> _dailyBars;
        LinkedListNode<Bar> _currentBarNode;

        Contract _contract;
        ILogger _logger;
        IBClient _client;

        int _reqId5SecBar = -1;

        public FakeClient(DateTime start, DateTime end, IEnumerable<Bar> dailyBars, IBClient client, ILogger logger)
        {
            _currentFakeTime = start;
            _start = start;
            _end = end; 
            _dailyBars = new LinkedList<Bar>(dailyBars);
            _currentBarNode = _dailyBars.First;
            _client = client;
            _logger = logger;
            _messageQueue = new ConcurrentQueue<Action>();

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
            StartConsumerTask();
            StartPassingTimeTask();
        }

        public void Stop()
        {
            CancelSubscriptions();
            StopPassingTimeTask();
            StopConsumerTask();
        }

        void CancelSubscriptions()
        {
            ClockTick -= OnClockTick_AccountSubscription;
            ClockTick -= OnClockTick_FiveSecondBar;
        }

        void StartConsumerTask()
        {
            _consumerTaskCancellation = new CancellationTokenSource();
            var token = _consumerTaskCancellation.Token;
            _consumerTask = Task.Factory.StartNew(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    if (_messageQueue.TryDequeue(out Action action))
                        action.Invoke();
                }

            }, token);
        }

        void StopConsumerTask()
        {
            _consumerTaskCancellation.Cancel();
            _consumerTaskCancellation.Dispose();
            _consumerTaskCancellation = null;
            _consumerTask = null;
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
                        ClockTick?.Invoke(_currentFakeTime);
                        Task.Delay(TimeDelays.OneSecond, delayToken).Wait();
                        
                        _currentFakeTime = _currentFakeTime.AddSeconds(1);
                        if (_currentFakeTime.Second % 5 == 0)
                            _currentBarNode = _currentBarNode.Next;
                    }
                    catch (OperationCanceledException)
                    {
                        delayCancellation.Dispose();
                        if (!mainToken.IsCancellationRequested)
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
            if (accountCode != _fakeAccount.Code)
                throw new InvalidOperationException();

            _messageQueue.Enqueue(SendAccountUpdate);

            if (receiveUpdates)
            {
                _lastAccountUpdate = _currentFakeTime;
                ClockTick += OnClockTick_AccountSubscription;
            }
            else
            {
                ClockTick -= OnClockTick_AccountSubscription;
            }
        }

        void OnClockTick_AccountSubscription(DateTime newTime)
        {
            if(newTime -_lastAccountUpdate >= TimeSpan.FromSeconds(3))
            {
                SendAccountUpdate();
            }
        }

        void ForceAccountUpdate() => SendAccountUpdate();

        void SendAccountUpdate()
        {
            var currentTime = _currentFakeTime;
            _lastAccountUpdate = currentTime;
            _messageQueue.Enqueue(() => 
            {
                //TODO : insert delay between calls?
                Callbacks.updateAccountTime(currentTime.ToString()); //TODO : make sure format is correct
                Callbacks.updateAccountValue("CashBalance", _fakeAccount.CashBalances.First().Value.ToString(), "USD", _fakeAccount.Code);
                Callbacks.accountDownloadEnd(_fakeAccount.Code);
            });
        }

        public void RequestContract(int reqId, Contract contract)
        {
            if (contract != _contract)
                throw new InvalidOperationException();

            var cd = new IBApi.ContractDetails();
            cd.Contract = _contract.ToIBApiContract();

            _messageQueue.Enqueue(() => 
            { 
                Callbacks.contractDetails(reqId, cd);
                Callbacks.contractDetailsEnd(reqId);
            });
        }

        public void RequestFiveSecondsBars(int reqId, Contract contract)
        {
            if (contract != _contract)
                throw new InvalidOperationException();

            _reqId5SecBar = reqId;
            ClockTick += OnClockTick_FiveSecondBar;
        }

        void OnClockTick_FiveSecondBar(DateTime newTime)
        {
            var b = _currentBarNode.Value;
            _messageQueue.Enqueue(() => 
            {
                DateTimeOffset dto = new DateTimeOffset(b.Time.ToUniversalTime());
                Callbacks.realtimeBar(_reqId5SecBar, dto.ToUnixTimeSeconds(), b.Open, b.High, b.Low, b.Close, b.Volume, 0, b.TradeAmount);
            });
        }

        public void CancelFiveSecondsBarsRequest(int reqId)
        {
            _reqId5SecBar = -1;
            ClockTick -= OnClockTick_FiveSecondBar;
        }

        public void RequestOpenOrders()
        {
            var openOrders = _openOrders;
            _messageQueue.Enqueue(() =>
            {
                foreach (var o in openOrders)
                    Callbacks.openOrder(o.Id, _contract.ToIBApiContract(), o.ToIBApiOrder(), new IBApi.OrderState() { Status = "Submitted" });
                Callbacks.openOrderEnd();
            });
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
    }
}
