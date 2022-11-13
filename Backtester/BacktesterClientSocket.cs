using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InteractiveBrokers;
using InteractiveBrokers.Accounts;
using InteractiveBrokers.Backend;
using InteractiveBrokers.Contracts;
using InteractiveBrokers.MarketData;
using InteractiveBrokers.Messages;
using InteractiveBrokers.Orders;
using NLog;

namespace Backtester
{
    internal class BacktesterClientSocket : IIBClientSocket
    {
        internal static class TimeDelays
        {
            public static double TimeScale = 0.001;
            public static int OneSecond => (int)Math.Round(1 * 1000 * TimeScale);
        }

        static ConcurrentDictionary<string, Contract> _contractsCache = new ConcurrentDictionary<string, Contract>();

        string _symbol;
        Contract _contract;

        IBClient _innerClient;

        Stopwatch _st = new Stopwatch();

        BlockingCollection<Action> _requestsQueue;
        BlockingCollection<Action> _responsesQueue;

        Task _requestsTask;
        Task _responsesTask;
        Task _passingTimeTask;
        CancellationTokenSource _cancellationTokenSource;

        DateTime _start;
        DateTime _end;
        DateTime _currentFakeTime;

        event Action<DateTime> ClockTick;
        event Action<BidAsk> BidAskSubscription;
        event Action<Last> LastSubscription;

        DateTime _lastAccountUpdate;
        Account _fakeAccount;
        
        bool _isConnected = false;
        int _nextValidOrderId = 1;
        int _nextExecId = 0;
        double _totalCommission = 0;

        List<Order> _openOrders = new List<Order>();
        List<Order> _executedOrders = new List<Order>();

        MarketDataCollections _dailyData;
        IEnumerator<Bar> _currentBar;
        IEnumerator<BidAsk> _currentBidAsk;
        IEnumerator<Last> _currentLast;

        ILogger _logger;

        bool _positionRequested = false;
        int _reqId5SecBar = -1;
        int _reqIdBidAsk = -1;
        int _reqIdLast = -1;
        int _reqIdPnL = -1;
        
        internal int NextValidOrderId => _nextValidOrderId++;
        int NextExecId => _nextExecId++;
        
        Position Position => _fakeAccount.Positions.FirstOrDefault();
        internal Contract Contract => _contract;
        internal Account Account => _fakeAccount;
        
        public BacktesterClientSocket(string symbol, DateTime startTime, DateTime endTime, MarketDataCollections dailyData)
        {
            _logger = LogManager.GetLogger(nameof(BacktesterClientSocket));
            Callbacks = new IBCallbacks(_logger);
            _requestsQueue = new BlockingCollection<Action>();
            _responsesQueue = new BlockingCollection<Action>();

            _symbol = symbol;
            _currentFakeTime = startTime;
            _start = startTime;
            _end = endTime;

            _dailyData = dailyData;
            _currentBar = GetStartEnumerator(_dailyData.Bars);
            _currentBidAsk = GetStartEnumerator(_dailyData.BidAsks);
            _currentLast = GetStartEnumerator(_dailyData.Lasts);

            _innerClient = new IBClient();
            _innerClient.ConnectAsync().Wait(2000);

            if (!_contractsCache.TryGetValue(symbol, out _contract))
            {
                _contract = _innerClient.GetContractAsync(symbol).Result;
                _contractsCache.TryAdd(symbol, _contract);
            }

            _fakeAccount = new Account()
            {
                Code = "FAKEACCOUNT123",
                CashBalances = new Dictionary<string, double>() 
                { 
                    { "BASE", 25000.00 },
                    { "USD", 25000.00 },
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

            _cancellationTokenSource = new CancellationTokenSource();
            _requestsTask = StartConsumerTask(_requestsQueue);
            _responsesTask = StartConsumerTask(_responsesQueue);
        }

        ~BacktesterClientSocket()
        {
            _innerClient.DisconnectAsync().Wait(2000);
        }

        public IBCallbacks Callbacks { get; private set; }

        internal Task PassingTimeTask => _passingTimeTask;

        IEnumerator<T> GetStartEnumerator<T>(IEnumerable<T> data) where T : IMarketData
        {
            if (data == null || !data.Any())
                throw new ArgumentException("no market data");

            var e = data.SkipWhile(d => d?.Time < _start).GetEnumerator();
            e.MoveNext();
            return e;
        }

        public void Connect(string host, int port, int clientId)
        {
            Start();
            _requestsQueue.Add(() =>
            {
                if (_isConnected)
                {
                    _responsesQueue.Add(() => Callbacks.error(new ErrorMessageException(501, "Already Connected.")));
                    return;
                }

                _isConnected = true;
                _responsesQueue.Add(() => Callbacks.nextValidId(NextValidOrderId));
                _responsesQueue.Add(() => Callbacks.managedAccounts(_fakeAccount.Code));
            });
        }

        public void Disconnect()
        {
            _requestsQueue.Add(() =>
            {
                _isConnected = false;
                _responsesQueue.Add(() => Callbacks.connectionClosed());
            });
        }

        internal void Start()
        {
            if (_passingTimeTask != null)
                return;

            _logger.Info($"Fake client started : {_currentFakeTime} to {_end}");

            ClockTick += OnClockTick_UpdateBar;
            ClockTick += OnClockTick_UpdateBidAsk;
            ClockTick += OnClockTick_UpdateLast;
            ClockTick += OnClockTick_UpdateUnrealizedPNL;
            _passingTimeTask = StartPassingTimeTask();
        }

        internal void Stop()
        {
            _logger.Info($"Fake client stopped at {_currentFakeTime}");

            StopPassingTimeTask();
            ClockTick -= OnClockTick_UpdateBar;
            ClockTick -= OnClockTick_UpdateBidAsk;
            ClockTick -= OnClockTick_UpdateLast;
            ClockTick -= OnClockTick_UpdateUnrealizedPNL;
        }

        Task StartConsumerTask(BlockingCollection<Action> collection)
        {
            var mainToken = _cancellationTokenSource.Token;
            var task = Task.Run(() =>
            {
                try
                {
                    while (true)
                    {
                        Action action = collection.Take(mainToken);
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
                    // Let's process the requests first before evaluating ClockTick callbacks
                    if (_requestsQueue.Count != 0)
                        continue;

                    ClockTick?.Invoke(_currentFakeTime);
                    if (TimeDelays.OneSecond > 0)
                        await Task.Delay(TimeDelays.OneSecond, mainToken);
                    _currentFakeTime = _currentFakeTime.AddSeconds(1);
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
            _requestsQueue.Add(() =>
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

            _requestsQueue.Add(() =>
            {
                var price = order.TotalQuantity * _currentBidAsk.Current.Ask;
                if(order.Action == OrderAction.BUY && _fakeAccount.CashBalances["BASE"] < price)
                {
                    _responsesQueue.Add(() => Callbacks.error(new ErrorMessageException(201, "Order rejected - Reason:")));
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
            _requestsQueue.Add(() =>
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
            _requestsQueue.Add(() =>
            {
                foreach (var o in _openOrders.ToList())
                    CancelOrderInternal(o.Id);
            });
        }

        public void RequestAccountUpdates(string accountCode)
        {
            if (accountCode != _fakeAccount.Code)
                throw new InvalidOperationException($"Can only return the fake account \"{_fakeAccount.Code}\"");

            _requestsQueue.Add(SendAccountUpdate);
            _requestsQueue.Add(() => ToggleAccountUpdates(true));
        }

        public void CancelAccountUpdates(string accountCode)
        {
            if (accountCode != _fakeAccount.Code)
                throw new InvalidOperationException($"Can only return the fake account \"{_fakeAccount.Code}\"");

            _requestsQueue.Add(() => ToggleAccountUpdates(false));
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
            if (_reqId5SecBar != -1)
                throw new NotSupportedException("Multiple requests not supported for now.");

            _requestsQueue.Add(() =>
            {
                if (_reqId5SecBar < 0)
                {
                    _logger.Debug($"(reqId={reqId}) : 5 sec bars requested.");
                    _reqId5SecBar = reqId;
                }
            });
        }

        void OnClockTick_UpdateBar(DateTime newTime)
        {
            if (_currentBar.Current.Time < newTime)
                _currentBar.MoveNext();

            if (_reqId5SecBar > 0 && newTime.Second % 5 == 0)
            {
                var list = new LinkedList<Bar>();
                var current = _currentBar;
                for (int i = 0; i < 5; i++)
                {
                    list.AddLast(current.Current);
                    current.MoveNext();
                }

                var b = Utils.CombineBars(list, BarLength._5Sec);
                DateTimeOffset dto = new DateTimeOffset(b.Time.ToUniversalTime());
                _responsesQueue.Add(() => Callbacks.realtimeBar(_reqId5SecBar, dto.ToUnixTimeSeconds(), b.Open, b.High, b.Low, b.Close, b.Volume, 0, b.TradeAmount));
            }
        }

        public void CancelFiveSecondsBarsUpdates(int reqId)
        {
            if (_reqId5SecBar == -1) 
                return;

            _requestsQueue.Add(() =>
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
            _requestsQueue.Add(() =>
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

        void OnClockTick_UpdateUnrealizedPNL(DateTime newTime)
        {
            var ba = _currentBidAsk.Current;
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
            _requestsQueue.Add(() =>
            {
                _positionRequested = true;
                _responsesQueue.Add(() => Callbacks.position(_fakeAccount.Code, Position.Contract.ToIBApiContract(), Position.PositionAmount, Position.AverageCost));
                _responsesQueue.Add(() => Callbacks.positionEnd());
            });
        }

        public void CancelPositionsUpdates()
        {
            _requestsQueue.Add(() => _positionRequested = false);
        }

        public void RequestPnLUpdates(int reqId, int contractId)
        {
            if (_reqIdPnL != -1)
                throw new NotSupportedException("Multiple requests not supported for now.");

            _requestsQueue.Add(() =>
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
            _requestsQueue.Add(() =>
            {
                ClockTick -= OnClockTick_PnL;
                _reqIdPnL = -1;
            });
        }

        public void RequestHistoricalData(int reqId, Contract contract, string endDateTime, string durationStr, string barSizeStr, bool onlyRTH)
        {
            // TODO : fetch from db if data is available

            _innerClient.Socket.Callbacks.HistoricalData = Callbacks.HistoricalData;
            _innerClient.Socket.Callbacks.HistoricalDataEnd = Callbacks.HistoricalDataEnd;
            _innerClient.Socket.RequestHistoricalData(reqId, contract, endDateTime, durationStr, barSizeStr, onlyRTH);
        }

        public void RequestHistoricalTicks(int reqId, Contract contract, string startDateTime, string endDateTime, int nbOfTicks, string whatToShow, bool onlyRTH, bool ignoreSize)
        {
            switch (whatToShow)
            {
                case "BID_ASK":
                    _innerClient.Socket.Callbacks.HistoricalTicksBidAsk = Callbacks.HistoricalTicksBidAsk;
                    break;

                case "TRADES":
                    _innerClient.Socket.Callbacks.HistoricalTicksLast = Callbacks.HistoricalTicksLast;
                    break;

                default: throw new NotImplementedException($"\"{whatToShow}\" tick type is not implemented");
            }

            _innerClient.Socket.RequestHistoricalTicks(reqId, contract, startDateTime, endDateTime, nbOfTicks, whatToShow, onlyRTH, ignoreSize);
        }

        public void RequestTickByTickData(int reqId, Contract contract, string tickType)
        {
            switch (tickType)
            {
                case "BidAsk":
                    RequestBidAskData(reqId);
                    break;

                case "Last":
                    RequestLastData(reqId);
                    break;

                default: throw new NotImplementedException($"\"{tickType}\" tick type is not implemented");
            }
        }

        void RequestBidAskData(int reqId)
        {
            if (_reqIdBidAsk != -1)
                throw new NotSupportedException("Multiple requests not supported for now.");

            _requestsQueue.Add(() =>
            {
                _reqIdBidAsk = reqId;

                if(BidAskSubscription == null)
                    BidAskSubscription += SendBidAsk;
            });
        }

        void SendBidAsk(BidAsk ba)
        {
            _responsesQueue.Add(() => Callbacks.tickByTickBidAsk(_reqIdBidAsk, new DateTimeOffset(ba.Time.ToUniversalTime()).ToUnixTimeSeconds(), ba.Bid, ba.Ask, ba.BidSize, ba.AskSize, new IBApi.TickAttribBidAsk()));
        }

        void OnClockTick_UpdateBidAsk(DateTime newTime)
        {
            // Since the lowest resolution is 1 second, all bid/asks that happen in between are delayed.
            while (_currentBidAsk.Current.Time < newTime)
            {
                EvaluateOpenOrders(_currentBidAsk.Current);
                BidAskSubscription?.Invoke(_currentBidAsk.Current);
                _currentBidAsk.MoveNext();
            }
        }

        void RequestLastData(int reqId)
        {
            if (_reqIdLast != -1)
                throw new NotSupportedException("Multiple requests not supported for now.");

            _requestsQueue.Add(() =>
            {
                _reqIdLast = reqId;
                if(LastSubscription == null)
                    LastSubscription += SendLast;
            });
        }

        void SendLast(Last last)
        {
            _responsesQueue.Add(() => Callbacks.tickByTickAllLast(_reqIdLast, 0, new DateTimeOffset(last.Time.ToUniversalTime()).ToUnixTimeSeconds(), last.Price, last.Size, new IBApi.TickAttribLast(), "", ""));
        }

        void OnClockTick_UpdateLast(DateTime newTime)
        {
            // Since the lowest resolution is 1 second, all bid/asks that happen in between are delayed...
            while (_currentLast.Current.Time < newTime)
            {
                LastSubscription?.Invoke(_currentLast.Current);
                _currentLast.MoveNext();
            }
        }

        public void CancelTickByTickData(int reqId)
        {
            if (reqId == -1) 
                return;

            _requestsQueue.Add(() =>
            {
                if(reqId == _reqIdBidAsk)
                {
                    BidAskSubscription -= SendBidAsk;
                    _reqIdBidAsk = -1;
                }
                else if (reqId == _reqIdLast)
                {
                    LastSubscription -= SendLast;
                    _reqIdLast = -1;
                }
            });
        }

        public void RequestValidOrderIds()
        {
            int next = NextValidOrderId;
            _requestsQueue.Add(() => _responsesQueue.Add(() => Callbacks.nextValidId(next)));
        }

        public void RequestContractDetails(int reqId, Contract contract)
        {
            _requestsQueue.Add(() =>
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
