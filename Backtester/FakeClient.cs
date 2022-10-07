using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using NLog;
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
        /// For market hours 7:00 to 16:00 : 9 hours = 32400s
        /// - To have 1 day pass in 1 hour => TimeScale = 3600.0d/32400 ~ 111 ms/sec
        /// - To have 1 week (5 days) pass in 1 hour => TimeScale = 3600.0d/162000 ~ 22 ms/sec
        /// </summary>
        internal static class TimeDelays
        {
            public static double TimeScale = 0.001;
            public static int OneSecond => (int)Math.Round(1 * 1000 * TimeScale);
        }
        Stopwatch _st = new Stopwatch();

        ConcurrentQueue<Action> _messageQueue;
        Task _consumerTask;
        CancellationTokenSource _consumerTaskCancellation;

        DateTime _start;
        DateTime _end;
        DateTime _currentFakeTime;

        event Action<DateTime> ClockTick;
        event Action<BidAsk> BidAskSubscription;
        Task _passingTimeTask;
        CancellationTokenSource _passingTimeCancellation;

        DateTime _lastAccountUpdate;
        Account _fakeAccount;
        
        int _nextValidOrderId = 1;
        int _nextExecId = 0;
        double _totalCommission = 0;

        List<Order> _openOrders = new List<Order>();
        List<Order> _executedOrders = new List<Order>();

        LinkedList<Bar> _5SecBars = new LinkedList<Bar>();
        LinkedList<Bar> _dailyBars;
        LinkedListNode<Bar> _currentBarNode;

        LinkedList<BidAsk> _dailyBidAsks;
        LinkedListNode<BidAsk> _currentBidAskNode;

        ILogger _logger;

        bool _positionRequested = false;
        int _reqId5SecBar = -1;
        int _reqIdBidAsk = -1;
        int _reqIdPnL = -1;
        
        internal int NextValidOrderId => _nextValidOrderId++;
        int NextExecId => _nextExecId++;
        Position Position => _fakeAccount.Positions.FirstOrDefault();
        internal Contract Contract => Position?.Contract;
        internal Account Account => _fakeAccount;
        internal LinkedListNode<BidAsk> CurrentBidAskNode => _currentBidAskNode;
        
        public FakeClient(Contract contract, DateTime startTime, DateTime endTime, IEnumerable<Bar> dailyBars, IEnumerable<BidAsk> dailyBidAsks)
        {
            _logger = LogManager.GetLogger(nameof(FakeClient));
            Callbacks = new IBCallbacks(_logger);
            _messageQueue = new ConcurrentQueue<Action>();
            
            _currentFakeTime = startTime;
            _start = startTime;
            _end = endTime;

            _dailyBars = new LinkedList<Bar>(dailyBars);
            _currentBarNode = InitFirstNode(_dailyBars);

            _dailyBidAsks = new LinkedList<BidAsk>(dailyBidAsks);
            _currentBidAskNode = InitFirstNode(_dailyBidAsks);

            _fakeAccount = new Account()
            {
                Code = "FAKEACCOUNT123",
                CashBalances = new Dictionary<string, double>() { { "USD", 5000.00 } },
                Positions = new List<Position>() { new Position() { Contract = contract } }
            };
        }

        public IBCallbacks Callbacks { get; private set; }

        public void WaitUntilDayIsOver() => _passingTimeTask.Wait();

        LinkedListNode<T> InitFirstNode<T>(LinkedList<T> list) where T : IMarketData
        {
            var current = list.First;
            while (current.Value.Time < _start)
                current = current.Next;
            return current;
        }

        public Task<bool> ConnectAsync(string host, int port, int clientId)
        {
            var tcs = new TaskCompletionSource<bool>();
            Start();
            tcs.SetResult(true);
            return tcs.Task;
        }

        public void Connect(string host, int port, int clientId)
        {
            ConnectAsync(host, port, clientId).Wait();  
        }

        public void Disconnect()
        {
            Stop();
        }

        internal void Start()
        {
            _logger.Info($"Fake client started : {_currentFakeTime} to {_end}");

            ClockTick += OnClockTick_UpdateBarNode;
            ClockTick += OnClockTick_UpdateBidAskNode;
            ClockTick += OnClockTick_UpdateUnrealizedPNL;
            StartConsumerTask();
            StartPassingTimeTask();
        }

        internal void Stop()
        {
            _logger.Info($"Fake client stopped at {_currentFakeTime}");

            ClockTick -= OnClockTick_UpdateBarNode;
            ClockTick -= OnClockTick_UpdateBidAskNode;
            ClockTick -= OnClockTick_UpdateUnrealizedPNL;
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
            _consumerTaskCancellation?.Cancel();
            _consumerTaskCancellation?.Dispose();
            _consumerTaskCancellation = null;
            _consumerTask = null;
        }

        void StartPassingTimeTask()
        {
            _passingTimeCancellation = new CancellationTokenSource();
            var mainToken = _passingTimeCancellation.Token;
            _st.Start();
            _logger.Trace($"Passing time task started");
            _passingTimeTask = Task.Factory.StartNew(() =>
            {
                var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(mainToken, CancellationToken.None);
                var delayToken = delayCancellation.Token;

                while (!mainToken.IsCancellationRequested && _currentFakeTime < _end)
                {
                    try
                    {
                        // TODO : Possible slowdown when time scale is really low...
                        ClockTick?.Invoke(_currentFakeTime);
                        Task.Delay(TimeDelays.OneSecond, delayToken).Wait();
                        _currentFakeTime = _currentFakeTime.AddSeconds(1);
                        //_logger.Info($"{_currentFakeTime}\t{_st.ElapsedMilliseconds}");
                    }
                    catch (AggregateException e)
                    {
                        //TODO : verify error handling
                        if(e.InnerException is OperationCanceledException)
                        {
                            delayCancellation.Dispose();
                            if (!mainToken.IsCancellationRequested)
                            {
                                delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(mainToken, CancellationToken.None);
                                delayToken = delayCancellation.Token;
                            }
                            else
                                _logger.Trace($"Passing time task cancelled");

                            return;
                        }
                        
                        throw e;
                    }
                }

            }, mainToken);
        }

        void StopPassingTimeTask()
        {
            _st.Stop();
            _passingTimeCancellation?.Cancel();
            _passingTimeCancellation?.Dispose();
            _passingTimeCancellation = null;
            _passingTimeTask = null;
        }

        public Task<long> GetCurrentTime()
        {
            var tcs = new TaskCompletionSource<long>();
            DateTimeOffset dto = new DateTimeOffset(_currentFakeTime.ToUniversalTime());
            tcs.SetResult(dto.ToUnixTimeSeconds());
            return tcs.Task;
        }

        public void PlaceOrder(Contract contract, Order order)
        {
            Debug.Assert(Position != null);
            Debug.Assert(order.Id > 0);

            var openOrder = _openOrders.FirstOrDefault(o => o == order);
            if (openOrder == null)
            {
                //TODO validate order : enough cash to buy, enough shares to sell
                _openOrders.Add(order);

                _logger.Debug($"New order submitted : {order}");
                _messageQueue.Enqueue(() =>
                {
                    var orderState = new IBApi.OrderState() { Status = "Submitted" };
                    Callbacks.openOrder(order.Id, contract.ToIBApiContract(), order.ToIBApiOrder(), orderState);
                });
            }
            else //modify order
            {
                //TODO : handle fees when modifying/cancelling order

                _logger.Debug($"Order modified : {order}");
                openOrder = order;
            }

            _messageQueue.Enqueue(() =>
            {
                //TODO : validate callback order. It should reflect what TWS does

                var orderState = new IBApi.OrderState() { Status = "Submitted" };
                Callbacks.orderStatus(order.Id, "Submitted", 0,0,0,0,0,0,0, "", 0);
            });
        }

        double GetCommission(Contract contract, Order order, double price)
        {
            //https://www.interactivebrokers.ca/en/index.php?f=1590

            // fixed rates
            double @fixed = 0.005;
            double min = 1.0;
            double max = order.TotalQuantity * price * 0.01; // 1% of trade value
                        
            return Math.Min(Math.Max(@fixed * order.TotalQuantity, min), max);
        }

        internal bool IsExecuted(Order order)
        {
            bool isExecuted = _executedOrders.Contains(order);
            if(isExecuted)
            {
                Trace.Assert(!_openOrders.Contains(order));
            }
            return isExecuted;
        }

        void EvaluateOpenOrders(BidAsk bidAsk)
        {
            foreach(Order o in _openOrders)
            {
                _logger.Debug($"Evaluating Order {o} at BidAsk : {bidAsk}");

                if (o is MarketOrder mo)
                {
                    EvaluateMarketOrder(bidAsk, mo);
                }
                else if (o is LimitOrder lo)
                {
                    EvaluateLimitOrder(bidAsk, lo);
                }
                else if(o is StopOrder so)
                {
                    EvaluateStopOrder(bidAsk, so);
                }
                else if (o is TrailingStopOrder tso)
                {
                    EvaluateTrailingStopOrder(bidAsk, tso);
                }
                else if(o is MarketIfTouchedOrder mito)
                {
                    EvaluateMarketIfTouchedOrder(bidAsk, mito);
                }
            }

            _openOrders.RemoveAll(o => _executedOrders.Contains(o));
        }

        private void EvaluateMarketOrder(BidAsk bidAsk, MarketOrder o)
        {
            if (o.Action == OrderAction.BUY)
            {
                ExecuteOrder(o, bidAsk.Ask);
            }
            else if (o.Action == OrderAction.SELL)
            {
                ExecuteOrder(o, bidAsk.Bid);
            }
        }

        private void EvaluateMarketIfTouchedOrder(BidAsk bidAsk, MarketIfTouchedOrder o)
        {
            if (o.Action == OrderAction.BUY && o.TouchPrice >= bidAsk.Bid)
            {
                _logger.Debug($"{o.OrderType} {o.Action} Order : touch price of {o.TouchPrice:c} reached. {bidAsk}");
                ExecuteOrder(o, bidAsk.Ask);
            }
            else if (o.Action == OrderAction.SELL && o.TouchPrice <= bidAsk.Ask)
            {
                _logger.Debug($"{o.OrderType} {o.Action} Order : touch price of {o.TouchPrice:c} reached. {bidAsk}");
                ExecuteOrder(o, bidAsk.Bid);
            }
        }

        private void EvaluateTrailingStopOrder(BidAsk bidAsk, TrailingStopOrder o)
        {
            //TODO : validate computations
            if (o.Action == OrderAction.BUY)
            {
                if (o.StopPrice == double.MaxValue)
                {
                    o.StopPrice = o.TrailingPercent != double.MaxValue ? 
                        bidAsk.Ask + o.TrailingPercent * bidAsk.Ask : 
                        bidAsk.Ask + o.TrailingAmount;
                }

                if (o.StopPrice <= bidAsk.Ask)
                {
                    _logger.Debug($"{o} : stop price of {o.StopPrice:c} reached. Ask : {bidAsk.Ask:c}");
                    ExecuteOrder(o, bidAsk.Ask);
                }
                else if (o.TrailingPercent != double.MaxValue)
                {
                    var currentPercent = (o.StopPrice - bidAsk.Ask) / bidAsk.Ask;
                    if (currentPercent > o.TrailingPercent)
                    {
                        var newVal = bidAsk.Ask +  o.TrailingPercent * bidAsk.Ask;
                        _logger.Trace($"{o} : current%={currentPercent} trail%={o.TrailingPercent} : adjusting stop price of {o.StopPrice:c} to {newVal:c}");
                        o.StopPrice = newVal;
                    }
                }
                else
                {
                    // The price must be updated if the ask falls
                    var currentStopPrice = bidAsk.Ask + o.TrailingAmount;
                    if (currentStopPrice < o.StopPrice)
                    {
                        o.StopPrice = currentStopPrice;
                        _logger.Trace($"{o} : adjusting stop price of {o.StopPrice:c} to {currentStopPrice:c}");
                    }
                }
            }
            else if (o.Action == OrderAction.SELL)
            {
                if (o.StopPrice == double.MaxValue)
                {
                    o.StopPrice = o.TrailingPercent != double.MaxValue ?
                        bidAsk.Bid - o.TrailingPercent * bidAsk.Bid : 
                        bidAsk.Bid - o.TrailingAmount;
                }

                if (o.StopPrice >= bidAsk.Bid)
                {
                    _logger.Debug($"{o} : stop price of {o.StopPrice:c} reached. Bid: {bidAsk.Bid:c}");
                    ExecuteOrder(o, bidAsk.Bid);
                }
                else if (o.TrailingPercent != double.MaxValue)
                {
                    var currentPercent = (o.StopPrice - bidAsk.Bid) / -bidAsk.Bid;
                    if (currentPercent > o.TrailingPercent)
                    {
                        var newVal = bidAsk.Bid - o.TrailingPercent * bidAsk.Bid;
                        _logger.Trace($"{o} : current%={currentPercent} trail%={o.TrailingPercent} : adjusting stop price of {o.StopPrice:c} to {newVal:c}");
                        o.StopPrice = newVal;
                    }
                }
                else
                {
                    // The price must be updated if the bid rises
                    var currentStopPrice = bidAsk.Bid - o.TrailingAmount;
                    if (currentStopPrice > o.StopPrice)
                    {
                        o.StopPrice = currentStopPrice;
                        _logger.Trace($"{o} : adjusting stop price of {o.StopPrice:c} to {currentStopPrice:c}");
                    }
                }
            }
        }

        private void EvaluateStopOrder(BidAsk bidAsk, StopOrder o)
        {
            if (o.Action == OrderAction.BUY && o.StopPrice <= bidAsk.Ask)
            {
                _logger.Debug($"{o} : stop price of {o.StopPrice:c} reached. {bidAsk}");
                ExecuteOrder(o, bidAsk.Ask);
            }
            else if (o.Action == OrderAction.SELL && o.StopPrice >= bidAsk.Bid)
            {
                _logger.Debug($"{o} : stop price of {o.StopPrice:c} reached. {bidAsk}");
                ExecuteOrder(o, bidAsk.Bid);
            }
        }

        private void EvaluateLimitOrder(BidAsk bidAsk, LimitOrder o)
        {
            if (o.Action == OrderAction.BUY && o.LmtPrice >= bidAsk.Ask)
            {
                _logger.Debug($"{o} : lmt price of {o.LmtPrice:c} reached. Ask : {bidAsk.Ask:c}");
                ExecuteOrder(o, bidAsk.Ask);
            }
            else if (o.Action == OrderAction.SELL && o.LmtPrice <= bidAsk.Bid)
            {
                _logger.Debug($"{o} : lmt price of {o.LmtPrice:c} reached. Bid : {bidAsk.Bid:c}");
                ExecuteOrder(o, bidAsk.Bid);
            }
        }

        void ExecuteOrder(Order order, double price)
        {
            _logger.Info($"{order} : Executing at price {price:c}");
            var total = order.TotalQuantity * price;

            if(order.Action == OrderAction.BUY)
            {
                Position.PositionAmount += order.TotalQuantity;
                Position.AverageCost = Position.AverageCost > 0 ? (Position.AverageCost + price) / 2 : price;
                _logger.Debug($"Account {_fakeAccount.Code} :  New position {Position.PositionAmount} at {Position.AverageCost:c}/shares");

                UpdateUnrealizedPNL(price);
                _fakeAccount.CashBalances["USD"] -= total;
            }
            else if (order.Action == OrderAction.SELL)
            {
                Position.PositionAmount -= order.TotalQuantity;
                _logger.Debug($"Account {_fakeAccount.Code} :  New position {Position.PositionAmount} at {Position.AverageCost:c}/shares");

                Position.RealizedPNL += order.TotalQuantity * (Position.MarketPrice - Position.AverageCost);
                _logger.Debug($"Account {_fakeAccount.Code} :  Realized PnL  : {Position.RealizedPNL:c}");

                UpdateUnrealizedPNL(price);
                _fakeAccount.CashBalances["USD"] += total;
            }

            var o = _openOrders.First(o => o == order);
            _executedOrders.Add(o);

            double commission = GetCommission(Contract, order, price);
            _logger.Debug($"{order} : commission : {commission:c}");

            _fakeAccount.CashBalances["USD"] -= commission;
            _totalCommission += commission;

            _logger.Debug($"Account {_fakeAccount.Code} :  New USD cash balance : {_fakeAccount.CashBalances["USD"]:c}");

            string execId = NextExecId.ToString();
            _messageQueue.Enqueue(() =>
            {
                //TODO : verify that orderStatus() is called on order execution
                Callbacks.orderStatus(order.Id, "Filled", o.TotalQuantity, 0, total, order.Id, order.Info.ParentId, price, 0, "", 0);

                var exec = new IBApi.Execution()
                {
                    ExecId = execId,
                    OrderId = o.Id,
                    Time = _currentFakeTime.ToString("yyyyMMdd  HH:mm:ss"),
                    AcctNumber = _fakeAccount.Code,
                    Exchange = Contract.Exchange,
                    Side = o.Action == OrderAction.BUY ? "BOT" : "SLD",
                    Shares = o.TotalQuantity,
                    Price = total,
                    AvgPrice = price
                };
                Callbacks.execDetails(o.Id, Contract.ToIBApiContract(), exec);

                Callbacks.commissionReport(new IBApi.CommissionReport() 
                {
                     Commission = commission,
                     Currency = "USD",
                     ExecId = execId,
                     RealizedPNL = Position.RealizedPNL,
                });
            });
        }

        public void CancelOrder(int orderId)
        {
            var order = _openOrders.First(o => o.Id == orderId);
            if(order != null)
            {
                _logger.Debug($"Order {orderId} cancelled.");
                _openOrders.Remove(order);
                _messageQueue.Enqueue(() =>
                {
                    Callbacks.orderStatus(order.Id, "Cancelled ", 0,0,0,0,0,0,0,"", 0);
                });
            }
            else
                _logger.Warn($"Cannot cancel order {orderId} (not found)");
        }

        public void CancelAllOrders() 
        {
            foreach (var o in _openOrders)
                CancelOrder(o.Id);
        }

        public Task<Account> GetAccountAsync()
        {
            var tcs = new TaskCompletionSource<Account>();
            ToggleAccountUpdates(true);
            tcs.SetResult(_fakeAccount);
            return tcs.Task;
        }

        public void RequestAccount(string accountCode, bool receiveUpdates = true)
        {
            if (accountCode != _fakeAccount.Code)
                throw new InvalidOperationException($"Can only return the fake account \"{_fakeAccount.Code}\"");

            _messageQueue.Enqueue(SendAccountUpdate);
            ToggleAccountUpdates(receiveUpdates);
        }

        void ToggleAccountUpdates(bool receiveUpdates)
        {
            if (receiveUpdates && _lastAccountUpdate == DateTime.MinValue)
            {
                _logger.Debug($"Account updates requested");
                _lastAccountUpdate = _currentFakeTime;
                ClockTick += OnClockTick_AccountSubscription;
            }
            else
            {
                _lastAccountUpdate = DateTime.MinValue;
                _logger.Debug($"Account updates cancelled");
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
            _logger.Debug($"Sending account updates...");
            var currentTime = _currentFakeTime;
            _lastAccountUpdate = currentTime;
            _messageQueue.Enqueue(() => 
            {
                Callbacks.updateAccountTime(currentTime.ToString()); //TODO : make sure format is correct
                Callbacks.updateAccountValue("CashBalance", _fakeAccount.CashBalances.First().Value.ToString(CultureInfo.InvariantCulture), "USD", _fakeAccount.Code);
                Callbacks.accountDownloadEnd(_fakeAccount.Code);
            });
        }

        public void RequestContract(int reqId, Contract contract)
        {
            var cd = new IBApi.ContractDetails();
            cd.Contract = contract.ToIBApiContract();
            _logger.Debug($"(reqId={reqId}) : Contract {contract} requested.");

            _messageQueue.Enqueue(() => 
            { 
                Callbacks.contractDetails(reqId, cd);
                Callbacks.contractDetailsEnd(reqId);
            });
        }

        public void RequestFiveSecondsBars(int reqId, Contract contract)
        {
            if(_reqId5SecBar < 0)
            {
                _logger.Debug($"(reqId={reqId}) : 5 sec bars requested.");
                _reqId5SecBar = reqId;
                ClockTick += OnClockTick_FiveSecondBar;
            }
        }

        void OnClockTick_FiveSecondBar(DateTime newTime)
        {
            if (newTime.Second % 5 == 0)
            {
                var b = MakeBar(_currentBarNode, BarLength._5Sec);
                _messageQueue.Enqueue(() => 
                {
                    DateTimeOffset dto = new DateTimeOffset(b.Time.ToUniversalTime());
                    Callbacks.realtimeBar(_reqId5SecBar, dto.ToUnixTimeSeconds(), b.Open, b.High, b.Low, b.Close, b.Volume, 0, b.TradeAmount);
                });
            }
        }

        Bar MakeBar(LinkedListNode<Bar> node, BarLength barLength)
        {
            Bar bar = new Bar() { High = double.MinValue, Low = double.MaxValue, BarLength = barLength };

            int nbBars = Convert.ToInt32(barLength);
            LinkedListNode<Bar> currNode = node;
            for (int i = 0; i < nbBars; i++)
            {
                Bar current = currNode.Value;
                if (i == 0)
                {
                    bar.Open = current.Open;
                    bar.Time = current.Time;
                }

                bar.High = Math.Max(bar.High, current.High);
                bar.Low = Math.Min(bar.Low, current.Low);
                bar.Volume += current.Volume;
                bar.TradeAmount += current.TradeAmount;

                if (i == nbBars - 1)
                {
                    bar.Close = current.Close;
                }

                currNode = currNode.Next;
            }

            return bar;
        }

        public void CancelFiveSecondsBarsRequest(int reqId)
        {
            if(reqId == _reqId5SecBar)
            {
                _logger.Debug($"(reqId={reqId}) : 5 sec bars cancelled.");
                _reqId5SecBar = -1;
                ClockTick -= OnClockTick_FiveSecondBar;
            }
        }

        public void RequestOpenOrders()
        {
            if (!_openOrders.Any())
                return;

            var openOrders = _openOrders;
            _messageQueue.Enqueue(() =>
            {
                foreach (var o in openOrders)
                    Callbacks.openOrder(o.Id, Contract.ToIBApiContract(), o.ToIBApiOrder(), new IBApi.OrderState() { Status = "Submitted" });
                Callbacks.openOrderEnd();
            });
        }

        void OnClockTick_UpdateBarNode(DateTime newTime)
        {
            if(_currentBarNode?.Value.Time < newTime)
            {
                _5SecBars.AddFirst(_currentBarNode.Value);
                if (_5SecBars.Count > 5)
                    _5SecBars.RemoveLast();
                _currentBarNode = _currentBarNode.Next;
            }
        }

        void OnClockTick_UpdateBidAskNode(DateTime newTime)
        {
            // Since the lowest resolution is 1 second, all bid/asks that happen in between are delayed.
            while (_currentBidAskNode?.Value.Time < newTime)
            {
                EvaluateOpenOrders(_currentBidAskNode.Value);
                BidAskSubscription?.Invoke(_currentBidAskNode.Value);
                _currentBidAskNode = _currentBidAskNode.Next;
            }
        }

        void OnClockTick_UpdateUnrealizedPNL(DateTime newTime)
        {
            var ba = _currentBidAskNode.Value;
            var currentPrice = ba.Ask;

            var oldPos = new Position(Position);
            UpdateUnrealizedPNL(currentPrice);

            if(_positionRequested && oldPos != Position)
            {
                SendPosition();
                ForceAccountUpdate();
            }
        }

        void UpdateUnrealizedPNL(double currentPrice)
        {
            Position.MarketPrice = currentPrice;
            Position.MarketValue = currentPrice * Position.PositionAmount;

            var positionValue = Position.PositionAmount * Position.AverageCost;
            Position.UnrealizedPNL = Position.MarketValue - positionValue;

            //_logger.Debug($"Account {_fakeAccount.Code} :  Unrealized PnL  : {Position.UnrealizedPNL:c}  (position value : {positionValue:c} market value : {Position.MarketValue:c})");
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
            _logger.Debug($"Sending current position for {Position.Contract}");
            _messageQueue.Enqueue(() =>
            {
                Callbacks.position(_fakeAccount.Code, Position.Contract.ToIBApiContract(), Position.PositionAmount, Position.AverageCost);
            });
        }

        public void CancelPositions()
        {
            _positionRequested = false;
        }

        public void RequestPnL(int reqId, int contractId)
        {
            //updates are returned to IBApi.EWrapper.pnlSingle approximately once per second
            _reqIdPnL = reqId;
            ClockTick += OnClockTick_PnL;
        }

        void OnClockTick_PnL(DateTime newTime)
        {
            _messageQueue.Enqueue(() => 
            {
                Callbacks.pnlSingle(_reqIdPnL, Convert.ToInt32(Position.PositionAmount), Position.RealizedPNL, Position.UnrealizedPNL, Position.RealizedPNL, Position.MarketValue);
            });
        }

        public void CancelPnL(int contractId)
        {
            ClockTick -= OnClockTick_PnL;
            _reqIdPnL = -1;
        }

        public Task<LinkedList<Bar>> GetHistoricalDataAsync(int reqId, Contract contract, BarLength barLength, DateTime endDateTime, int count)
        {
            var tcs = new TaskCompletionSource<LinkedList<Bar>>();

            if(endDateTime != default(DateTime))
                throw new NotImplementedException("Can only request historical data from the current moment in this Fake client");

            LinkedList<Bar> list = new LinkedList<Bar>();
            LinkedListNode<Bar> first = _currentBarNode;
            LinkedListNode<Bar> current = first;

            int nbBars = count * (int)barLength;
            for (int i = 0; i <= nbBars; i++, current = current.Previous)
            {
                if(i != 0 && i % (int)barLength == 0)
                {
                    list.AddFirst(MakeBar(current, barLength));
                }
            }

            tcs.SetResult(list);
            return tcs.Task;
        }

        public void RequestHistoricalData(int reqId, Contract contract, string endDateTime, string durationStr, string barSizeStr, bool onlyRTH)
        {
            int nbBars = -1;
            switch(barSizeStr)
            {
                case "5 secs": nbBars = Convert.ToInt32(durationStr.Split()[0]) / 5; break;
                case "1 min": nbBars = Convert.ToInt32(durationStr.Split()[0]) / 60; break;
                    throw new NotImplementedException("Only \"5 secs\" or \"1 min\" historical data is implemented");
            }

            if(!string.IsNullOrEmpty(endDateTime))
                throw new NotImplementedException("Can only request historical data from the current moment in this Fake client");

            LinkedListNode<Bar> first = _currentBarNode;
            LinkedListNode<Bar> current = first;

            for (int i = 0; i < nbBars; i++, current = current.Previous)
            {
                _messageQueue.Enqueue(() => 
                {
                    Callbacks.historicalData(reqId, new IBApi.Bar(
                        current.Value.Time.ToString(Bar.TWSTimeFormat), 
                        current.Value.Open, 
                        current.Value.High, 
                        current.Value.Low, 
                        current.Value.Close, 
                        current.Value.Volume, 
                        current.Value.TradeAmount, 
                        0));
                });
            }

            _messageQueue.Enqueue(() =>
            {
                Callbacks.historicalDataEnd(reqId, first.Value.Time.ToString(Bar.TWSTimeFormat), current.Value.Time.ToString(Bar.TWSTimeFormat));
            });
        }

        public Task<IEnumerable<BidAsk>> RequestHistoricalTicks(int reqId, Contract contract, DateTime time, int count)
        {
            throw new NotImplementedException();
        }

        public void RequestTickByTickData(int reqId, Contract contract, string tickType)
        {
            if (tickType != "BidAsk")
                throw new NotImplementedException("Only \"BidAsk\" tick by tick data is implemented");

            _reqIdBidAsk = reqId;
            BidAskSubscription += SendBidAsk;
        }

        void SendBidAsk(BidAsk ba)
        {
            _messageQueue.Enqueue(() =>
            {
                Callbacks.tickByTickBidAsk(_reqIdBidAsk, new DateTimeOffset(ba.Time.ToUniversalTime()).ToUnixTimeSeconds(), ba.Bid, ba.Ask, ba.BidSize, ba.AskSize, new IBApi.TickAttribBidAsk());
            });
        }

        public void CancelTickByTickData(int reqId)
        {
            BidAskSubscription -= SendBidAsk;
            _reqIdBidAsk = -1;
        }

        public void RequestValidOrderIds()
        {
            int next = NextValidOrderId;
            _messageQueue.Enqueue(() => Callbacks.nextValidId(next));
        }

        public Task<List<Contract>> GetContractsAsync(int reqId, Contract contract)
        {
            var tcs = new TaskCompletionSource<List<Contract>>();
            tcs.SetResult(new List<Contract>() { Contract });
            return tcs.Task;
        }

        public void RequestAvailableFunds(int reqId)
        {
            _messageQueue.Enqueue(() => Callbacks.AccountSummary(reqId, _fakeAccount.Code, "AvailableFunds", _fakeAccount.CashBalances["USD"].ToString(), "USD"));
            _messageQueue.Enqueue(() => Callbacks.AccountSummaryEnd(reqId));
        }
    }
}
