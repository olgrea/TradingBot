using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        /// <summary>
        /// For supposed market hours 7:00 to 16:00 : 9 hours = 32400s
        /// - To have 1 day pass in 1 hour => TimeScale = 3600.0d/32400 ~ 111 ms/sec
        /// - To have 1 week (5 days) pass in 1 hour => TimeScale = 3600.0d/162000 ~ 22 ms/sec
        /// </summary>
        static class TimeDelays
        {
            public static double TimeScale = 3600.0d / 32400;
            public static int OneSecond = (int)Math.Round(1 * 1000 * TimeScale);
        }
        Stopwatch _st = new Stopwatch();

        ConcurrentQueue<Action> _messageQueue;
        Task _consumerTask;
        CancellationTokenSource _consumerTaskCancellation;

        bool _initialized = false;
        DateTime _start;
        DateTime _end;
        DateTime _currentFakeTime;

        event Action<DateTime> ClockTick;
        Task _passingTimeTask;
        CancellationTokenSource _passingTimeCancellation;

        DateTime _lastAccountUpdate;
        Account _fakeAccount;

        //TODO : use IBApi classes?
        Dictionary<Contract, Order> _openOrders = new Dictionary<Contract, Order>();
        Position _position = new Position();
        Dictionary<Contract, Order> _executedOrders = new Dictionary<Contract, Order>();

        LinkedList<Bar> _dailyBars;
        LinkedListNode<Bar> _currentBarNode;

        ILogger _logger;
        IBClient _client;

        bool _positionRequested = false;
        int _reqId5SecBar = -1;
        int _reqIdPnL = -1;

        int _nextValidOrderId = 0;
        int NextValidOrderId => _nextValidOrderId++;

        public FakeClient(IBClient client, ILogger logger)
        {
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

        public void WaitUntilDayIsOver() => _passingTimeTask.Wait();

        public void Init(DateTime startTime, DateTime endTime, IEnumerable<Bar> dailyBars)
        {
            _currentFakeTime = startTime;
            _start = startTime;
            _end = endTime;
            _dailyBars = new LinkedList<Bar>(dailyBars);
            _currentBarNode = _dailyBars.First;
            _initialized = true;
        }

        public void Connect(string host, int port, int clientId)
        {
            _client.Connect(host, port, clientId);
            Start();
        }

        public void Disconnect()
        {
            Stop();
            _client.Disconnect();
        }

        void Start()
        {
            if (!_initialized)
                throw new InvalidOperationException("Fake client has not been initialized.");

            ClockTick += OnClockTick_UpdateUnrealizedPNL;

            StartConsumerTask();
            StartPassingTimeTask();
        }

        void Stop()
        {
            ClockTick -= OnClockTick_AccountSubscription;
            ClockTick -= OnClockTick_FiveSecondBar;
            ClockTick -= OnClockTick_PnL;

            StopPassingTimeTask();
            StopConsumerTask();
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

                _st.Start();
                while (!mainToken.IsCancellationRequested && _currentFakeTime < _end)
                {
                    try
                    {
                        ClockTick?.Invoke(_currentFakeTime);
                        Task.Delay(TimeDelays.OneSecond, delayToken).Wait();
                        
                        _currentFakeTime = _currentFakeTime.AddSeconds(1);
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
            _st.Stop();
            _passingTimeCancellation.Cancel();
            _passingTimeCancellation.Dispose();
            _passingTimeCancellation = null;
            _passingTimeTask = null;
        }

        public void PlaceOrder(Contract contract, Order order)
        {

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
            var cd = new IBApi.ContractDetails();
            cd.Contract = contract.ToIBApiContract();

            _messageQueue.Enqueue(() => 
            { 
                Callbacks.contractDetails(reqId, cd);
                Callbacks.contractDetailsEnd(reqId);
            });
        }

        public void RequestFiveSecondsBars(int reqId, Contract contract)
        {
            _reqId5SecBar = reqId;
            ClockTick += OnClockTick_FiveSecondBar;
        }

        void OnClockTick_FiveSecondBar(DateTime newTime)
        {
            var elapsed = _st.ElapsedMilliseconds;
            //_logger.LogDebug($"newTime={newTime}\t{elapsed}");

            if(newTime.Second % 5 == 0)
            {
                var b = _currentBarNode.Value;
                _messageQueue.Enqueue(() => 
                {
                    DateTimeOffset dto = new DateTimeOffset(b.Time.ToUniversalTime());
                    Callbacks.realtimeBar(_reqId5SecBar, dto.ToUnixTimeSeconds(), b.Open, b.High, b.Low, b.Close, b.Volume, 0, b.TradeAmount);
                });
            }
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
                    Callbacks.openOrder(o.Value.Id, o.Key.ToIBApiContract(), o.Value.ToIBApiOrder(), new IBApi.OrderState() { Status = "Submitted" });
                Callbacks.openOrderEnd();
            });
        }

        void OnClockTick_UpdateUnrealizedPNL(DateTime newTime)
        {
            var newBar = _currentBarNode.Value;
            var currentPrice = (newBar.Close + newBar.High + newBar.Low) / 3;

            var oldPos = new Position(_position);
            _position.MarketPrice = currentPrice;
            _position.MarketValue = currentPrice * _position.PositionAmount;
            _position.UnrealizedPNL = _position.PositionAmount * (_position.MarketValue - _position.AverageCost);

            if(_positionRequested && oldPos != _position)
                SendPosition();
        }

        public void RequestPositions()
        {
            _positionRequested = true;
            SendPosition();
            _messageQueue.Enqueue(() =>
            {
                Callbacks.positionEnd();
            });
        }

        void SendPosition()
        {
            _messageQueue.Enqueue(() =>
            {
                Callbacks.position(_fakeAccount.Code, _position.Contract.ToIBApiContract(), _position.PositionAmount, _position.AverageCost);
            });
        }

        public void CancelPositions()
        {
            _positionRequested = false;
        }

        public void RequestPnL(int reqId, string accountCode, int contractId)
        {
            //updates are returned to IBApi.EWrapper.pnlSingle approximately once per second
            _reqIdPnL = reqId;
            ClockTick += OnClockTick_PnL;
        }

        void OnClockTick_PnL(DateTime newTime)
        {
            _messageQueue.Enqueue(() => 
            {
                Callbacks.pnlSingle(_reqIdPnL, Convert.ToInt32(_position.PositionAmount), _position.RealizedPNL, _position.UnrealizedPNL, _position.RealizedPNL, _position.MarketValue);
            });
        }

        public void CancelPnL(int contractId)
        {
            ClockTick -= OnClockTick_PnL;
            _reqIdPnL = -1;
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
            int next = NextValidOrderId;
            _messageQueue.Enqueue(() => Callbacks.nextValidId(next));
        }

        public void RequestHistoricalTicks(int reqId, Contract contract, string startDateTime, string endDateTime, int nbOfTicks, string whatToShow, bool onlyRTH, bool ignoreSize)
        {
            throw new NotImplementedException();
        }
    }
}
