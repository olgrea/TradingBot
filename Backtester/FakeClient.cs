using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using TradingBot.Broker;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.Client.Messages;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Utils;

namespace Backtester
{
    class ContractsCache
    {
        ConcurrentDictionary<string, Contract> _contracts = new ConcurrentDictionary<string, Contract>();    

        public Contract Get(string symbol)
        {
            return _contracts.GetOrAdd(symbol, symbol =>
            {
                return FetchContract(symbol).Result;
            });

            async Task<Contract> FetchContract(string symbol)
            {
                var broker = new IBBroker();
                await broker.ConnectAsync();
                await Task.Delay(50);
                var contract = await broker.GetContractAsync(symbol);
                await broker.DisconnectAsync();
                await Task.Delay(50);
                return contract;
            }
        }
    }

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

        static ContractsCache s_ContractsCache = new ContractsCache();
        string _symbol;
        Contract _contract;

        Stopwatch _st = new Stopwatch();

        ConcurrentQueue<Action> _requestsQueue;
        BlockingCollection<Action> _responsesQueue;

        Task _responsesTask;
        Task _passingTimeTask;
        CancellationTokenSource _cancellationTokenSource;

        DateTime _start;
        DateTime _end;
        DateTime _currentFakeTime;

        event Action<DateTime> ClockTick;
        event Action<BidAsk> BidAskSubscription;

        DateTime _lastAccountUpdate;
        Account _fakeAccount;
        
        bool isConnected = false;
        int _nextValidOrderId = 1;
        int _nextExecId = 0;
        double _totalCommission = 0;

        List<Order> _openOrders = new List<Order>();
        List<Order> _executedOrders = new List<Order>();

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
        internal Contract Contract => _contract;
        internal Account Account => _fakeAccount;

        internal LinkedListNode<BidAsk> CurrentBidAskNode => _currentBidAskNode;
        
        public FakeClient(string symbol, DateTime startTime, DateTime endTime, IEnumerable<Bar> dailyBars, IEnumerable<BidAsk> dailyBidAsks)
        {
            _logger = LogManager.GetLogger(nameof(FakeClient));
            Callbacks = new IBCallbacks(_logger);
            _requestsQueue = new ConcurrentQueue<Action>();
            _responsesQueue = new BlockingCollection<Action>();

            _symbol = symbol;
            _currentFakeTime = startTime;
            _start = startTime;
            _end = endTime;

            _dailyBars = new LinkedList<Bar>(dailyBars);
            _currentBarNode = InitFirstNode(_dailyBars);

            _dailyBidAsks = new LinkedList<BidAsk>(dailyBidAsks);
            _currentBidAskNode = InitFirstNode(_dailyBidAsks);

            _contract = s_ContractsCache.Get(_symbol);

            _fakeAccount = new Account()
            {
                Code = "FAKEACCOUNT123",
                CashBalances = new Dictionary<string, double>() 
                { 
                    { "BASE", 5000.00 },
                    { "USD", 5000.00 },
                },
                UnrealizedPnL = new Dictionary<string, double>()
                {
                    { "BASE", 0.00},
                    { "USD", 0.00 },
                },
                RealizedPnL= new Dictionary<string, double>()
                {
                    { "BASE", 0.00 },
                    { "USD", 0.00 },
                },
                Positions = new List<Position>() { new Position() { Contract = _contract } }
            };
        }

        public IBCallbacks Callbacks { get; private set; }

        internal Task PassingTimeTask => _passingTimeTask;

        LinkedListNode<T> InitFirstNode<T>(LinkedList<T> list) where T : IMarketData
        {
            var current = list.First;
            while (current.Value.Time < _start)
                current = current.Next;
            return current;
        }

        public void Connect(string host, int port, int clientId)
        {
            Start();
            _requestsQueue.Enqueue(() =>
            {
                if (isConnected)
                {
                    _responsesQueue.Add(() => Callbacks.error(new ErrorMessageException(501, "Already Connected.")));
                    return;
                }

                isConnected = true;
                _responsesQueue.Add(() => Callbacks.nextValidId(NextValidOrderId));
                _responsesQueue.Add(() => Callbacks.managedAccounts(_fakeAccount.Code));
            });
        }

        public void Disconnect()
        {
            _requestsQueue.Enqueue(() =>
            {
                isConnected = false;
                _responsesQueue.Add(() => Callbacks.connectionClosed());
            });
        }

        internal void Start()
        {
            _logger.Info($"Fake client started : {_currentFakeTime} to {_end}");

            ClockTick += OnClockTick_UpdateBarNode;
            ClockTick += OnClockTick_UpdateBidAskNode;
            ClockTick += OnClockTick_UpdateUnrealizedPNL;

            _cancellationTokenSource = new CancellationTokenSource();
            _responsesTask = StartResponsesTask();
            _passingTimeTask = StartPassingTimeTask();
        }

        internal void Stop()
        {
            _logger.Info($"Fake client stopped at {_currentFakeTime}");

            ClockTick -= OnClockTick_UpdateBarNode;
            ClockTick -= OnClockTick_UpdateBidAskNode;
            ClockTick -= OnClockTick_UpdateUnrealizedPNL;
            StopPassingTimeTask();
        }

        Task StartResponsesTask()
        {
            var mainToken = _cancellationTokenSource.Token;
            var task = Task.Run(() =>
            {
                try
                {
                    while (true)
                    {
                        Action action = _responsesQueue.Take(mainToken);
                        action();
                    }
                }
                catch (OperationCanceledException) {}
            }, mainToken);

            return task;
        }

        async Task StartPassingTimeTask()
        {
            var mainToken = _cancellationTokenSource.Token;
            _st.Start();
            _logger.Trace($"Passing time task started");
            while (!mainToken.IsCancellationRequested && _currentFakeTime < _end)
            {
                try
                {
                    while(_requestsQueue.TryDequeue(out Action action))
                        action.Invoke();

                    // TODO : Possible slowdown when time scale is really low...
                    ClockTick?.Invoke(_currentFakeTime);
                    await Task.Delay(TimeDelays.OneSecond, mainToken);
                    _currentFakeTime = _currentFakeTime.AddSeconds(1);
                    //_logger.Info($"{_currentFakeTime}\t{_st.ElapsedMilliseconds}");
                }
                catch (OperationCanceledException) {}
            }
        }

        void StopPassingTimeTask()
        {
            _st.Stop();
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _passingTimeTask = null;
        }

        public void RequestCurrentTime()
        {
            _requestsQueue.Enqueue(() =>
            {
                DateTimeOffset dto = new DateTimeOffset(_currentFakeTime.ToUniversalTime());
                _responsesQueue.Add(() => Callbacks.currentTime(dto.ToUnixTimeSeconds()));
            });
        }

        public void PlaceOrder(Contract contract, Order order)
        {
            if (order?.Id <= 0)
                throw new ArgumentException("Order id not set");
            
            Debug.Assert(Position != null);
            Debug.Assert(!_executedOrders.Contains(order));

            _requestsQueue.Enqueue(() =>
            {
                var price = order.TotalQuantity * _currentBidAskNode.Value.Ask;
                if(order.Action == OrderAction.BUY && _fakeAccount.CashBalances["BASE"] < price)
                {
                    // TODO : add reason string during market hours
                    _responsesQueue.Add(() => Callbacks.error(new ErrorMessageException(201, "Order rejected - Reason:")));
                    return;
                }

                if (order.Action == OrderAction.SELL && Position.PositionAmount < order.TotalQuantity)
                {
                    // TODO : no idea what TWS is supposed to return
                    //_responsesQueue.Add(() => Callbacks.error(new ErrorMessageException(201, "Order rejected - Reason:")));
                    return;
                }

                var openOrder = _openOrders.FirstOrDefault(o => o == order);
                if (openOrder == null)
                {
                    _openOrders.Add(order);

                    _logger.Debug($"New order submitted : {order}");
                    var c = contract.ToIBApiContract();
                    var o = order.ToIBApiOrder();
                    _responsesQueue.Add(() => Callbacks.openOrder(order.Id, c, o, new IBApi.OrderState() { Status = "PreSubmitted" }));
                    _responsesQueue.Add(() => Callbacks.openOrder(order.Id, c, o, new IBApi.OrderState() { Status = "Submitted" }));
                }
                else //modify order
                {
                    //TODO : handle fees when modifying/cancelling order?
                    _logger.Debug($"Order modified : {order}");
                    openOrder = order;
                }

                _responsesQueue.Add(() => Callbacks.orderStatus(order.Id, "PreSubmitted", 0, 0, 0, 0, 0, 0, 0, "", 0));
                _responsesQueue.Add(() => Callbacks.orderStatus(order.Id, "Submitted", 0, 0, 0, 0, 0, 0, 0, "", 0));
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
            foreach(Order o in _openOrders.ToList())
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

            Debug.Assert(!_executedOrders.Contains(order));

            if (order.Action == OrderAction.BUY)
            {
                if (total > _fakeAccount.CashBalances["USD"])
                {
                    _logger.Error($"{order} Cannot execute BUY order! Not enough funds (required : {total}, actual : {_fakeAccount.CashBalances["USD"]}");
                    CancelOrder(order.Id);
                    return;
                }

                Position.AverageCost = Position.PositionAmount != 0 ? (Position.AverageCost + price) / 2 : price;
                Position.PositionAmount += order.TotalQuantity;

                _logger.Debug($"Account {_fakeAccount.Code} :  New position {Position.PositionAmount} at {Position.AverageCost:c}/shares");

                UpdateUnrealizedPNL(price);
                UpdateCashBalance(-total);
            }
            else if (order.Action == OrderAction.SELL)
            {
                if (Position.PositionAmount < order.TotalQuantity)
                {
                    _logger.Error($"{order} Cannot execute SELL order! Not enough position (required : {order.TotalQuantity}, actual : {Position.PositionAmount}");
                    CancelOrderInternal(order.Id);
                    return;
                }

                Position.PositionAmount -= order.TotalQuantity;
                _logger.Debug($"Account {_fakeAccount.Code} :  New position {Position.PositionAmount} at {Position.AverageCost:c}/shares");

                UpdateRealizedPNL(order.TotalQuantity, price);
                UpdateUnrealizedPNL(price);
                UpdateCashBalance(total);
            }

            var o = _openOrders.First(o => o == order);
            _executedOrders.Add(o);
            _openOrders.Remove(o);

            double commission = UpdateCommissions(order, price);

            _logger.Debug($"Account {_fakeAccount.Code} :  New USD cash balance : {_fakeAccount.CashBalances["USD"]:c}");

            string execId = NextExecId.ToString();
            //TODO : verify that orderStatus() is called on order execution
            _responsesQueue.Add(() => Callbacks.orderStatus(order.Id, "Filled", o.TotalQuantity, 0, total, order.Id, order.Info.ParentId, price, 0, "", 0));

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
            _responsesQueue.Add(() => Callbacks.execDetails(o.Id, Contract.ToIBApiContract(), exec));

            _responsesQueue.Add(() => Callbacks.commissionReport(new IBApi.CommissionReport()
            {
                Commission = commission,
                Currency = "USD",
                ExecId = execId,
                RealizedPNL = Position.RealizedPNL,
            }));
        }

        private double UpdateCommissions(Order order, double price)
        {
            double commission = GetCommission(Contract, order, price);
            _logger.Debug($"{order} : commission : {commission:c}");

            UpdateCashBalance(-commission);
            _totalCommission += commission;
            return commission;
        }

        private void UpdateCashBalance(double total)
        {
            _fakeAccount.CashBalances["BASE"] += total;
            _fakeAccount.CashBalances["USD"] += total;
        }

        private void UpdateRealizedPNL(double totalQty, double price)
        {
            var realized = totalQty * (price - Position.AverageCost);

            Position.RealizedPNL += realized;
            _fakeAccount.RealizedPnL["BASE"] += realized;
            _fakeAccount.RealizedPnL["USD"] += realized;

            _logger.Debug($"Account {_fakeAccount.Code} :  Realized PnL  : {Position.RealizedPNL:c}");
        }

        public void CancelOrder(int orderId)
        {
            _requestsQueue.Enqueue(() =>
            {
                CancelOrderInternal(orderId);
            });
        }

        private void CancelOrderInternal(int orderId)
        {
            var order = _openOrders.FirstOrDefault(o => o.Id == orderId);
            if (order == null)
            {
                _logger.Warn($"Cannot cancel order {orderId} (not found)");
                return;
            }
            
            _logger.Debug($"Order {orderId} cancelled.");
            _openOrders.Remove(order);
            _responsesQueue.Add(() => Callbacks.orderStatus(order.Id, "Cancelled ", 0, 0, 0, 0, 0, 0, 0, "", 0));
            _responsesQueue.Add(() => Callbacks.error(orderId, 202, "Order Canceled - reason:"));
        }

        public void CancelAllOrders() 
        {
            _requestsQueue.Enqueue(() =>
            {
                foreach (var o in _openOrders.ToList())
                    CancelOrderInternal(o.Id);
            });
        }

        public void RequestAccountUpdates(string accountCode)
        {
            if (accountCode != _fakeAccount.Code)
                throw new InvalidOperationException($"Can only return the fake account \"{_fakeAccount.Code}\"");

            _requestsQueue.Enqueue(SendAccountUpdate);
            _requestsQueue.Enqueue(() => ToggleAccountUpdates(true));
        }

        public void CancelAccountUpdates(string accountCode)
        {
            if (accountCode != _fakeAccount.Code)
                throw new InvalidOperationException($"Can only return the fake account \"{_fakeAccount.Code}\"");

            _requestsQueue.Enqueue(() => ToggleAccountUpdates(false));
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
            _lastAccountUpdate = _currentFakeTime;

            foreach(var balance in _fakeAccount.CashBalances)
                _responsesQueue.Add(() => Callbacks.updateAccountValue("CashBalance", balance.Value.ToString(CultureInfo.InvariantCulture), balance.Key, _fakeAccount.Code));

            foreach (var pnl in _fakeAccount.RealizedPnL)
                _responsesQueue.Add(() => Callbacks.updateAccountValue("RealizedPnL", pnl.Value.ToString(CultureInfo.InvariantCulture), pnl.Key, _fakeAccount.Code));

            foreach (var pnl in _fakeAccount.UnrealizedPnL)
                _responsesQueue.Add(() => Callbacks.updateAccountValue("UnrealizedPnL", pnl.Value.ToString(CultureInfo.InvariantCulture), pnl.Key, _fakeAccount.Code));


            foreach (var p in _fakeAccount.Positions)
            {
                _responsesQueue.Add(() =>
                {
                    Callbacks.updatePortfolio(Contract.ToIBApiContract(), p.PositionAmount, p.MarketPrice, p.MarketValue, p.AverageCost, p.UnrealizedPNL, p.RealizedPNL, _fakeAccount.Code);
                    Callbacks.updateAccountTime(_currentFakeTime.ToShortTimeString());
                });
            }

            _responsesQueue.Add(() =>
            {
                Callbacks.updateAccountTime(_currentFakeTime.ToShortTimeString());
                Callbacks.accountDownloadEnd(_fakeAccount.Code);
            });
        }

        public void RequestFiveSecondsBarUpdates(int reqId, Contract contract)
        {
            _requestsQueue.Enqueue(() =>
            {
                if (_reqId5SecBar < 0)
                {
                    _logger.Debug($"(reqId={reqId}) : 5 sec bars requested.");
                    _reqId5SecBar = reqId;
                }
            });
        }

        void OnClockTick_UpdateBarNode(DateTime newTime)
        {
            if (_currentBarNode?.Value.Time < newTime)
                _currentBarNode = _currentBarNode.Next;

            if (_reqId5SecBar > 0 && newTime.Second % 5 == 0)
            {
                var b = MarketDataUtils.MakeBar(_currentBarNode, 5);
                DateTimeOffset dto = new DateTimeOffset(b.Time.ToUniversalTime());
                _responsesQueue.Add(() => Callbacks.realtimeBar(_reqId5SecBar, dto.ToUnixTimeSeconds(), b.Open, b.High, b.Low, b.Close, b.Volume, 0, b.TradeAmount));
            }
        }

        public void CancelFiveSecondsBarsUpdates(int reqId)
        {
            _requestsQueue.Enqueue(() =>
            {
                if (reqId == _reqId5SecBar)
                {
                    _logger.Debug($"(reqId={reqId}) : 5 sec bars cancelled.");
                    _reqId5SecBar = -1;
                }
            });
        }

        public void RequestOpenOrders()
        {
            _requestsQueue.Enqueue(() =>
            {
                SendOpenOrders();
            });
        }

        private void SendOpenOrders()
        {
            if (!_openOrders.Any())
                return;

            var openOrders = _openOrders;
            foreach (var o in openOrders)
            {
                _responsesQueue.Add(() => Callbacks.openOrder(o.Id, Contract.ToIBApiContract(), o.ToIBApiOrder(), new IBApi.OrderState() { Status = "Submitted" }));
            }

            _responsesQueue.Add(() => Callbacks.openOrderEnd());
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
                Callbacks.position(_fakeAccount.Code, Position.Contract.ToIBApiContract(), Position.PositionAmount, Position.AverageCost);
                ForceAccountUpdate();
            }
        }

        void UpdateUnrealizedPNL(double currentPrice)
        {
            Position.MarketPrice = currentPrice;
            Position.MarketValue = currentPrice * Position.PositionAmount;

            var positionValue = Position.PositionAmount * Position.AverageCost;
            var unrealizedPnL = Position.MarketValue - positionValue;

            Position.UnrealizedPNL = unrealizedPnL;
            _fakeAccount.UnrealizedPnL["USD"] = unrealizedPnL;
            _fakeAccount.UnrealizedPnL["BASE"] = unrealizedPnL;

            //_logger.Debug($"Account {_fakeAccount.Code} :  Unrealized PnL  : {Position.UnrealizedPNL:c}  (position value : {positionValue:c} market value : {Position.MarketValue:c})");
        }

        public void RequestPositionsUpdates()
        {
            _requestsQueue.Enqueue(() =>
            {
                _positionRequested = true;
                _responsesQueue.Add(() => Callbacks.position(_fakeAccount.Code, Position.Contract.ToIBApiContract(), Position.PositionAmount, Position.AverageCost));
                _responsesQueue.Add(() => Callbacks.positionEnd());
            });
        }

        public void CancelPositionsUpdates()
        {
            _requestsQueue.Enqueue(() => _positionRequested = false);
        }

        public void RequestPnLUpdates(int reqId, int contractId)
        {
            _requestsQueue.Enqueue(() =>
            {
                //updates are returned to IBApi.EWrapper.pnlSingle approximately once per second
                _reqIdPnL = reqId;
                ClockTick += OnClockTick_PnL;
            });
        }

        void OnClockTick_PnL(DateTime newTime)
        {
            // TODO : check to see if daily pnl include commission
            _responsesQueue.Add(() => Callbacks.pnlSingle(_reqIdPnL, Convert.ToInt32(Position.PositionAmount), Position.RealizedPNL - _totalCommission, Position.UnrealizedPNL, Position.RealizedPNL, Position.MarketValue));
        }

        public void CancelPnLUpdates(int contractId)
        {
            _requestsQueue.Enqueue(() =>
            {
                ClockTick -= OnClockTick_PnL;
                _reqIdPnL = -1;
            });
        }

        public void RequestHistoricalData(int reqId, Contract contract, string endDateTime, string durationStr, string barSizeStr, bool onlyRTH)
        {
            if(!string.IsNullOrEmpty(endDateTime))
                throw new NotImplementedException("Can only request historical data from the current moment in this Fake client");
            
            _requestsQueue.Enqueue(() => 
            {
                int nbBars = -1;
                switch(barSizeStr)
                {
                    case "5 secs": nbBars = Convert.ToInt32(durationStr.Split()[0]) / 5; break;
                    case "1 min": nbBars = Convert.ToInt32(durationStr.Split()[0]) / 60; break;
                        throw new NotImplementedException("Only \"5 secs\" or \"1 min\" historical data is implemented");
                }

                LinkedListNode<Bar> first = _currentBarNode;
                LinkedListNode<Bar> current = first;

                for (int i = 0; i < nbBars; i++, current = current.Previous)
                {
                    _responsesQueue.Add(() => Callbacks.historicalData(reqId, new IBApi.Bar(
                        current.Value.Time.ToString(Bar.TWSTimeFormat), 
                        current.Value.Open, 
                        current.Value.High, 
                        current.Value.Low, 
                        current.Value.Close, 
                        current.Value.Volume, 
                        current.Value.TradeAmount, 
                        0)));
                }

                _responsesQueue.Add(() => Callbacks.historicalDataEnd(reqId, first.Value.Time.ToString(Bar.TWSTimeFormat), current.Value.Time.ToString(Bar.TWSTimeFormat)));
            });
        }

        public void RequestHistoricalTicks(int reqId, Contract contract, string startDateTime, string endDateTime, int nbOfTicks, string whatToShow, bool onlyRTH, bool ignoreSize)
        {
            throw new NotImplementedException();
        }

        public void RequestTickByTickData(int reqId, Contract contract, string tickType)
        {
            if (tickType != "BidAsk")
                throw new NotImplementedException("Only \"BidAsk\" tick by tick data is implemented");

            _requestsQueue.Enqueue(() =>
            {
                _reqIdBidAsk = reqId;
                BidAskSubscription += SendBidAsk;
            });
        }

        void SendBidAsk(BidAsk ba)
        {
            _responsesQueue.Add(() => Callbacks.tickByTickBidAsk(_reqIdBidAsk, new DateTimeOffset(ba.Time.ToUniversalTime()).ToUnixTimeSeconds(), ba.Bid, ba.Ask, ba.BidSize, ba.AskSize, new IBApi.TickAttribBidAsk()));
        }

        public void CancelTickByTickData(int reqId)
        {
            _requestsQueue.Enqueue(() =>
            {
                BidAskSubscription -= SendBidAsk;
                _reqIdBidAsk = -1;
            });
        }

        public void RequestValidOrderIds()
        {
            int next = NextValidOrderId;
            _requestsQueue.Enqueue(() => _responsesQueue.Add(() => Callbacks.nextValidId(next)));
        }

        public void RequestContractDetails(int reqId, Contract contract)
        {
            _requestsQueue.Enqueue(() =>
            {
                if(contract.Symbol != Contract.Symbol)
                {
                    _responsesQueue.Add(() => Callbacks.error(reqId, 200, "No security definition has been found for the request"));
                    return;
                }

                var details = new IBApi.ContractDetails()
                {
                    Contract = Contract.ToIBApiContract(),
                };

                _responsesQueue.Add(() => Callbacks.contractDetails(reqId, details));
                _responsesQueue.Add(() => Callbacks.contractDetailsEnd(reqId));
            });
        }
    }
}
