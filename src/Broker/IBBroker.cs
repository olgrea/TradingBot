using System;
using System.Linq;
using System.Collections.Generic;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using NLog;
using System.Diagnostics;
using TradingBot.Indicators;
using TradingBot.Utils;
using TradingBot.Broker.Client.Messages;
using System.Globalization;
using System.Threading;

[assembly: InternalsVisibleTo("HistoricalDataFetcher")]
[assembly: InternalsVisibleTo("Tests")]
namespace TradingBot.Broker
{
    internal class DataSubscriptions
    {
        public bool AccountUpdates { get; set; }
        public bool Positions { get; set; }

        //TODO : remove all that "by Contract" stuff?
        public Dictionary<Contract, int> BidAsk { get; set; } = new Dictionary<Contract, int>();
        public Dictionary<Contract, int> FiveSecBars { get; set; } = new Dictionary<Contract, int>();
        public Dictionary<Contract, int> Pnl { get; set; } = new Dictionary<Contract, int>();
    }

    internal class IBBroker : IBroker
    {
        static Random rand = new Random();

        const int DefaultTWSPort = 7496;
        const int DefaultIBGatewayPort = 4002;
        const string DefaultIP = "127.0.0.1";

        int _port;
        int _clientId = 1337;
        int _reqId = 0;
        DataSubscriptions _subscriptions = new DataSubscriptions();
        IIBClient _client;
        ILogger _logger;
        OrderManager _orderManager;
        Dictionary<Contract, LinkedList<MarketData.Bar>> _fiveSecBars = new Dictionary<Contract, LinkedList<MarketData.Bar>>();

        int NextRequestId => _reqId++;
        internal DataSubscriptions Subscriptions => _subscriptions;

        #region Events

        // TODO : revert back to dictionary of Action<> ?
        event Action<Contract, Bar> Bar5SecReceived;
        event Action<Contract, Bar> Bar1MinReceived;
        public event Action<Contract, BidAsk> BidAskReceived;

        public event Action<string, string, string, string> AccountValueUpdated
        {
            add => _client.Callbacks.UpdateAccountValue += value;
            remove => _client.Callbacks.UpdateAccountValue -= value;
        }

        public event Action<Position> PositionReceived
        {
            add => _client.Callbacks.Position += value;
            remove => _client.Callbacks.Position -= value;
        }

        public event Action<PnL> PnLReceived;
        
        public event Action<Order, OrderStatus> OrderUpdated
        {
            add => _orderManager.OrderUpdated += value;
            remove => _orderManager.OrderUpdated -= value;
        }

        public event Action<OrderExecution, CommissionInfo> OrderExecuted
        {
            add => _orderManager.OrderExecuted += value;
            remove => _orderManager.OrderExecuted -= value;
        }

        public event Action<long> CurrentTimeReceived
        {
            add => _client.Callbacks.CurrentTime += value;
            remove => _client.Callbacks.CurrentTime -= value;
        }

        public IErrorHandler ErrorHandler
        {
            get => _client.Callbacks.ErrorHandler;
            set => _client.Callbacks.ErrorHandler = value;
        }

        #endregion Events

        public IBBroker()
        {
            Init(rand.Next(), null, null);
        }

        public IBBroker(int clientId)
        {
            Init(clientId, null, null);
        }

        internal IBBroker(int clientId, IIBClient client)
        {
            Init(clientId, client, null);
        }

        void Init(int clientId, IIBClient client, ILogger logger)
        {
            _port = GetPort();
            _clientId = clientId;
            _logger = logger ?? LogManager.GetLogger($"{nameof(IBBroker)}-{_clientId}"); 
            _client = client ?? new IBClient(_logger);            
            _orderManager = new OrderManager(this, _client, _logger);
        }

        int GetPort()
        {
            var ibGatewayProc = Process.GetProcessesByName("ibgateway").FirstOrDefault();
            if(ibGatewayProc != null)
                return DefaultIBGatewayPort;

            var twsProc = Process.GetProcessesByName("tws").FirstOrDefault();
            if (twsProc != null)
                return DefaultTWSPort;

            throw new ArgumentException("Neither TWS Workstation or IB Gateway is running.");
        }

        public Task<ConnectMessage> ConnectAsync()
        {
            return ConnectAsync(Debugger.IsAttached ? -1 : 5000);
        }

        public Task<ConnectMessage> ConnectAsync(int timeoutInMs)
        {
            //TODO: Handle IB server resets
            var msg = new ConnectMessage();
            var tcs = new TaskCompletionSource<ConnectMessage>();
            CancellationTokenSource source = new CancellationTokenSource();
            source.Token.Register(() => tcs.TrySetException(new TimeoutException($"{nameof(ConnectAsync)}")));

            var nextValidId = new Action<int>(id =>
            {
                _logger.Trace($"ConnectAsync : next valid id {id}");
                msg.NextValidOrderId = id;
                
                if(msg.IsSet())
                    tcs.TrySetResult(msg);
            });

            var managedAccounts = new Action<string>(acc =>
            {
                _logger.Trace($"ConnectAsync : managedAccounts {acc} - set result");
                msg.AccountCode = acc;
                
                if (msg.IsSet())
                    tcs.TrySetResult(msg);
            });

            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _client.Callbacks.NextValidId += nextValidId;
            _client.Callbacks.ManagedAccounts += managedAccounts;
            _client.Callbacks.Error += error;

            source.CancelAfter(timeoutInMs);
            _client.Connect(DefaultIP, _port, _clientId);

            tcs.Task.ContinueWith(t =>
            {
                _client.Callbacks.NextValidId -= nextValidId;
                _client.Callbacks.ManagedAccounts -= managedAccounts;
                _client.Callbacks.Error -= error;
            });

            return tcs.Task;
        }

        public Task<bool> DisconnectAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            _logger.Debug($"Disconnecting from TWS");

            var disconnect = new Action(() => tcs.SetResult(true));
            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _client.Callbacks.ConnectionClosed += disconnect;
            _client.Callbacks.Error += error;
            tcs.Task.ContinueWith(t =>
            {
                _client.Callbacks.ConnectionClosed -= disconnect;
                _client.Callbacks.Error -= error;
            });

            _client.Disconnect();

            return tcs.Task;
        }

        public Task<int> GetNextValidOrderIdAsync()
        {
            var tcs = new TaskCompletionSource<int>();

            var nextValidId = new Action<int>(id =>
            {
                tcs.SetResult(id);
            });
            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _client.Callbacks.NextValidId += nextValidId;
            _client.Callbacks.Error += error;
            tcs.Task.ContinueWith(t =>
            {
                _client.Callbacks.NextValidId -= nextValidId;
                _client.Callbacks.Error -= error;
            });

            _logger.Debug($"Requesting next valid order ids");
            _client.RequestValidOrderIds();
            return tcs.Task;
        }

        public async Task<Account> GetAccountAsync(string accountCode)
        {
            var account = new Account() { Code = accountCode };

            var tcs = new TaskCompletionSource<bool>();

            var updateAccountTime = new Action<string>(time =>
            {
                _logger.Trace($"GetAccountAsync updateAccountTime : {time}");
                account.Time = DateTime.Parse(time, CultureInfo.InvariantCulture);
            });
            var updateAccountValue = new Action<string, string, string, string>((key, value, currency, acc) =>
            {
                _logger.Trace($"GetAccountAsync updateAccountValue : key={key}, value={value}");
                switch (key)
                {
                    case "CashBalance":
                        account.CashBalances[currency] = double.Parse(value, CultureInfo.InvariantCulture);
                        break;

                    case "RealizedPnL":
                        account.RealizedPnL[currency] = double.Parse(value, CultureInfo.InvariantCulture);
                        break;

                    case "UnrealizedPnL":
                        account.UnrealizedPnL[currency] = double.Parse(value, CultureInfo.InvariantCulture);
                        break;
                }
            });
            var updatePortfolio = new Action<Position>(pos =>
            {
                _logger.Trace($"GetAccountAsync updatePortfolio : {pos}");
                account.Positions.Add(pos);
            });
            var accountDownloadEnd = new Action<string>(accountCode =>
            {
                _logger.Trace($"GetAccountAsync accountDownloadEnd : {accountCode} - set result");
                tcs.SetResult(true);
            });

            _client.Callbacks.UpdateAccountTime += updateAccountTime;
            _client.Callbacks.UpdateAccountValue += updateAccountValue;
            _client.Callbacks.UpdatePortfolio += updatePortfolio;
            _client.Callbacks.AccountDownloadEnd += accountDownloadEnd;

            _client.RequestAccountUpdates(accountCode);
            
            await tcs.Task.ContinueWith(t =>
            {
                _client.Callbacks.UpdateAccountTime -= updateAccountTime;
                _client.Callbacks.UpdateAccountValue -= updateAccountValue;
                _client.Callbacks.UpdatePortfolio -= updatePortfolio;
                _client.Callbacks.AccountDownloadEnd -= accountDownloadEnd;

                _client.CancelAccountUpdates(accountCode);
            });

            return account;
        }

        public async Task<Contract> GetContractAsync(string symbol)
        {
            var sampleContract = new Stock()
            {
                Currency = "USD",
                Exchange = "SMART",
                Symbol = symbol,
                SecType = "STK"
            };

            var contractDetails = await GetContractDetailsAsync(sampleContract);
            return contractDetails?.FirstOrDefault().Contract;
        }

        public Task<List<ContractDetails>> GetContractDetailsAsync(Contract contract)
        {
            var reqId = NextRequestId;

            var tcs = new TaskCompletionSource<List<ContractDetails>>();
            var tmpDetails = new List<ContractDetails>();
            var contractDetails = new Action<int, ContractDetails>((rId, c) =>
            {
                if (rId == reqId)
                {
                    _logger.Trace($"GetContractsAsync temp step : adding {c}");
                    tmpDetails.Add(c);
                }
            });
            var contractDetailsEnd = new Action<int>(rId =>
            {
                if (rId == reqId)
                {
                    _logger.Trace($"GetContractsAsync end step : set result");
                    tcs.SetResult(tmpDetails);
                }
            });
            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _client.Callbacks.ContractDetails += contractDetails;
            _client.Callbacks.ContractDetailsEnd += contractDetailsEnd;
            _client.Callbacks.Error += error;

            tcs.Task.ContinueWith(t =>
            {
                _client.Callbacks.ContractDetails -= contractDetails;
                _client.Callbacks.ContractDetailsEnd -= contractDetailsEnd;
                _client.Callbacks.Error -= error;
            });

            _logger.Debug($"Requesting contract details for {contract} (reqId={reqId})");
            _client.RequestContractDetails(reqId, contract);

            return tcs.Task;
        }

        public void RequestBidAskUpdates(Contract contract)
        {
            if (_subscriptions.BidAsk.ContainsKey(contract))
                return;

            int reqId = NextRequestId;
            _subscriptions.BidAsk[contract] = reqId;
            if(_subscriptions.BidAsk.Count == 1)
                _client.Callbacks.TickByTickBidAsk += TickByTickBidAsk;

            _client.RequestTickByTickData(reqId, contract, "BidAsk");
        }

        void TickByTickBidAsk(int reqId, BidAsk bidAsk)
        {
            var contract = _subscriptions.BidAsk.First(c => c.Value == reqId).Key;
            BidAskReceived?.Invoke(contract, bidAsk);
        }

        public void CancelBidAskUpdates(Contract contract)
        {
            if (!_subscriptions.BidAsk.ContainsKey(contract))
                return;

            var reqId = _subscriptions.BidAsk[contract];
            _subscriptions.BidAsk.Remove(contract);
            if (_subscriptions.BidAsk.Count == 0)
                _client.Callbacks.TickByTickBidAsk += TickByTickBidAsk;

            _client.CancelTickByTickData(reqId);
        }

        public void RequestBarsUpdates(Contract contract)
        {
            if (_subscriptions.FiveSecBars.ContainsKey(contract))
                return;

            int reqId = NextRequestId;
            _subscriptions.FiveSecBars[contract] = reqId;
            _fiveSecBars[contract] = new LinkedList<Bar>();
            if (_subscriptions.FiveSecBars.Count == 1)
                _client.Callbacks.RealtimeBar += OnFiveSecondsBarReceived;

            // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
            _client.RequestFiveSecondsBarUpdates(reqId, contract);
        }

        void OnFiveSecondsBarReceived(int reqId, MarketData.Bar bar)
        {
            Trace.Assert(_subscriptions.FiveSecBars.ContainsValue(reqId));

            var contract = _subscriptions.FiveSecBars.First(c => c.Value == reqId).Key;

            _logger.Debug($"FiveSecondsBarReceived for {contract}");
            
            UpdateBarsAndInvoke(contract, bar);
        }

        void UpdateBarsAndInvoke(Contract contract, Bar bar)
        {
            var list = _fiveSecBars[contract];
            list.AddLast(bar);
            // keeping 5 minutes of bars
            if (list.Count > 60)
                list.RemoveFirst();

            foreach (BarLength barLength in Enum.GetValues(typeof(BarLength)).OfType<BarLength>())
            {
                if (!HasSubscribers(barLength))
                    continue;

                if (barLength == BarLength._5Sec)
                {
                    Bar5SecReceived?.Invoke(contract, bar);
                    continue;
                }

                int sec = (int)barLength;
                int nbBars = (sec / 5);
                if (list.Count > nbBars && (bar.Time.Second % sec) == 0)
                {
                    LinkedListNode<Bar> current = list.Last;
                    for (int i = 0; i < nbBars; ++i)
                        current = current.Previous;

                    var newBar = MarketDataUtils.MakeBar(current, nbBars);
                    InvokeCallbacks(contract, newBar);
                }
            }
        }

        Bar MakeBar(LinkedList<Bar> list, BarLength barLength)
        {
            _logger.Trace($"Making a {barLength}s bar using {list.Count} 5s bars");
            int seconds = (int)barLength;

            Bar bar = new Bar() { High = double.MinValue, Low = double.MaxValue, BarLength = barLength };
            var e = list.GetEnumerator();
            e.MoveNext();

            // The 1st bar shouldn't be included... don't remember why
            e.MoveNext();

            int nbBars = seconds / 5;
            for (int i = 0; i < nbBars; i++, e.MoveNext())
            {
                Bar current = e.Current;
                if (i == 0)
                {
                    bar.Close = current.Close;
                }

                bar.High = Math.Max(bar.High, current.High);
                bar.Low = Math.Min(bar.Low, current.Low);
                bar.Volume += current.Volume;
                bar.TradeAmount += current.TradeAmount;

                if (i == nbBars - 1)
                {
                    bar.Open = current.Open;
                    bar.Time = current.Time;
                }
            }

            return bar;
        }

        void InvokeCallbacks(Contract contract, MarketData.Bar bar)
        {
            switch (bar.BarLength)
            {
                case BarLength._5Sec:
                    _logger.Trace($"Invoking Bar5SecReceived for {contract}");
                    Bar5SecReceived?.Invoke(contract, bar); 
                    break;
                
                case BarLength._1Min:
                    _logger.Trace($"Invoking Bar1MinReceived for {contract}");
                    Bar1MinReceived?.Invoke(contract, bar); 
                    break;
            }
        }

        public void SubscribeToBarUpdateEvent(BarLength barLength, Action<Contract, Bar> callback)
        {
            switch (barLength)
            {
                case BarLength._5Sec: Bar5SecReceived += callback; break;
                case BarLength._1Min: Bar1MinReceived += callback; break;
                default: throw new NotImplementedException();
            }
        }

        public void UnsubscribeToBarUpdateEvent(BarLength barLength, Action<Contract, Bar> callback)
        {
            switch (barLength)
            {
                case BarLength._5Sec: Bar5SecReceived -= callback; break;
                case BarLength._1Min: Bar1MinReceived -= callback; break;
                default: throw new NotImplementedException();
            }
        }

        bool HasSubscribers(BarLength barLength)
        {
            switch(barLength)
            {
                case BarLength._5Sec: return Bar5SecReceived != null;
                case BarLength._1Min: return Bar1MinReceived != null;
                default: return false;
            }
        }

        public void CancelBarsUpdates(Contract contract)
        {
            if (!_subscriptions.FiveSecBars.ContainsKey(contract))
                return;

            var reqId = _subscriptions.FiveSecBars[contract];
            _subscriptions.FiveSecBars.Remove(contract);
            _fiveSecBars.Remove(contract);
            
            if(_subscriptions.FiveSecBars.Count == 0)
                _client.Callbacks.RealtimeBar -= OnFiveSecondsBarReceived;

            _client.CancelFiveSecondsBarsUpdates(reqId);
        }

        public Task<OrderMessage> PlaceOrderAsync(Contract contract, Order order)
        {
            return PlaceOrderAsync(contract, order, Debugger.IsAttached ? -1 : 5000);
        }

        public Task<OrderMessage> PlaceOrderAsync(Contract contract, Order order, int timeoutInMs)
        {
            if (order?.Id <= 0)
                throw new ArgumentException("Order id not set");

            var orderPlacedMsg = new OrderPlacedMessage();
            var orderExecutedMsg = new OrderExecutedMessage();

            var tcs = new TaskCompletionSource<OrderMessage>();
            var source = new CancellationTokenSource();
            source.Token.Register(() => tcs.TrySetException(new TimeoutException($"{nameof(PlaceOrderAsync)}")));
            
            var openOrder = new Action<Contract, Orders.Order, Orders.OrderState>((c, o, oState) =>
            {
                if (order.Id == o.Id)
                {
                    orderPlacedMsg.Contract = orderExecutedMsg.Contract = c;
                    orderPlacedMsg.Order = orderExecutedMsg.Order = o;
                    orderPlacedMsg.OrderState = oState;
                }
            });

            // With market orders, when the order is accepted and executes immediately, there commonly will not be any
            // corresponding orderStatus callbacks. For that reason it is recommended to also monitor the IBApi.EWrapper.execDetails .
            var orderStatus = new Action<OrderStatus>(oStatus =>
            {
                if (order.Id == oStatus.Info.OrderId && (oStatus.Status == Status.PreSubmitted || oStatus.Status == Status.Submitted))
                {
                    orderPlacedMsg.OrderStatus = oStatus;
                    tcs.TrySetResult(orderPlacedMsg);
                }
            });

            var execDetails = new Action<Contract, OrderExecution>((c, oe) =>
            {
                if (order.Id == oe.OrderId)
                    orderExecutedMsg.OrderExecution = oe;
            });

            var commissionReport = new Action<CommissionInfo>(ci =>
            {
                if (orderExecutedMsg.OrderExecution.ExecId == ci.ExecId)
                {
                    orderExecutedMsg.CommissionInfo = ci;
                    tcs.TrySetResult(orderExecutedMsg);
                }
            });

            var error = new Action<ErrorMessageException>(msg =>
            {
                if (!MarketDataUtils.IsMarketOpen() && msg.ErrorCode == 399 && msg.Message.Contains("your order will not be placed at the exchange until"))
                {
                    return;
                }

                tcs.TrySetException(msg);
            });

            _client.Callbacks.OpenOrder += openOrder;
            _client.Callbacks.OrderStatus += orderStatus;
            _client.Callbacks.ExecDetails += execDetails;
            _client.Callbacks.CommissionReport += commissionReport;
            _client.Callbacks.Error += error;

            source.CancelAfter(timeoutInMs);
            _client.PlaceOrder(contract, order);

            tcs.Task.ContinueWith(t =>
            {
                _client.Callbacks.OpenOrder -= openOrder;
                _client.Callbacks.OrderStatus -= orderStatus;
                _client.Callbacks.CommissionReport -= commissionReport;
                _client.Callbacks.ExecDetails -= execDetails;
                _client.Callbacks.Error -= error;
            });

            return tcs.Task;
        }

        public void PlaceOrder(Contract contract, Order order)
        {
            _orderManager.PlaceOrder(contract, order);
        }

        public void PlaceOrder(Contract contract, OrderChain chain)
        {
            PlaceOrder(contract, chain, false);
        }

        public void PlaceOrder(Contract contract, OrderChain chain, bool useTWSAttachedOrderFeature = false)
        {
            _orderManager.PlaceOrder(contract, chain, useTWSAttachedOrderFeature);
        }

        public void ModifyOrder(Contract contract, Order order)
        {
            _orderManager.ModifyOrder(contract, order);
        }

        public Task<OrderStatus> CancelOrderAsync(int orderId)
        {
            if (orderId <= 0)
                throw new ArgumentException("Invalid order id");

            var tcs = new TaskCompletionSource<OrderStatus>();
            var source = new CancellationTokenSource();
            source.Token.Register(() => tcs.TrySetException(new TimeoutException($"{nameof(CancelOrderAsync)}")));
            int timeoutInMs = Debugger.IsAttached ? -1 : 5000;

            var orderStatus = new Action<OrderStatus>(oStatus =>
            {
                if (orderId == oStatus.Info.OrderId)
                {
                    if (!tcs.Task.IsCompleted && (oStatus.Status == Status.ApiCancelled || oStatus.Status == Status.Cancelled))
                        tcs.TrySetResult(oStatus);
                }
            });

            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _client.Callbacks.OrderStatus += orderStatus;
            _client.Callbacks.Error += error;
            tcs.Task.ContinueWith(t =>
            {
                _client.Callbacks.OrderStatus -= orderStatus;
                _client.Callbacks.Error -= error;
            });

            _logger.Debug($"Requesting order cancellation for order id : {orderId}");

            source.CancelAfter(timeoutInMs);
            _client.CancelOrder(orderId);

            return tcs.Task;
        }

        public void CancelOrder(Order order)
        {
            _orderManager.CancelOrder(order);
        }

        public void CancelAllOrders() => _orderManager.CancelAllOrders();
        public bool HasBeenRequested(Order order) => _orderManager.HasBeenRequested(order);
        public bool HasBeenOpened(Order order) => _orderManager.HasBeenOpened(order);
        public bool IsCancelled(Order order) => _orderManager.IsCancelled(order);
        public bool IsExecuted(Order order, out OrderExecution orderExecution) => _orderManager.IsExecuted(order, out orderExecution);

        public void RequestPositionsUpdates()
        {
            _client.RequestPositionsUpdates();
            _subscriptions.Positions = true;
        }

        public void CancelPositionsUpdates()
        {
            _subscriptions.Positions = false;
            _client.CancelPositionsUpdates();
        }

        public void RequestPnLUpdates(Contract contract)
        {
            if (_subscriptions.Pnl.ContainsKey(contract))
                return;

            int reqId = NextRequestId;
            _subscriptions.Pnl[contract] = reqId;
            if(_subscriptions.Pnl.Count == 1)
                _client.Callbacks.PnlSingle += PnlSingle;

            _client.RequestPnLUpdates(reqId, contract.Id);
        }

        void PnlSingle(int reqId, PnL pnl)
        {
            pnl.Contract = _subscriptions.Pnl.First(s => s.Value == reqId).Key;
            PnLReceived?.Invoke(pnl);
        }

        public void CancelPnLUpdates(Contract contract)
        {
            if (!_subscriptions.Pnl.ContainsKey(contract))
                return;
            
            _subscriptions.Pnl.Remove(contract);
            if (_subscriptions.Pnl.Count == 0)
                _client.Callbacks.PnlSingle -= PnlSingle;

            _client.CancelPnLUpdates(_subscriptions.Pnl[contract]);
        }

        public void RequestAccountUpdates(string account)
        {
            _subscriptions.AccountUpdates = true;
            _client.RequestAccountUpdates(account);
        }

        public void CancelAccountUpdates(string account)
        {
            if (_subscriptions.AccountUpdates)
            {
                _client.CancelAccountUpdates(account);
            }
        }

        public async void InitIndicators(Contract contract, IEnumerable<IIndicator> indicators)
        {
            if (!indicators.Any())
                return;

            var longestTime = indicators.Max(i => i.NbPeriods * (int)i.BarLength);
            var pastBars = await GetPastBars(contract, BarLength._5Sec, longestTime/(int)BarLength._5Sec);

            //TODO : remove bars from indicators? I don't know what I was thinking...
            foreach(Bar bar in pastBars)
            {
                UpdateBarsAndInvoke(contract, bar);
            }
        }

        internal async Task<IEnumerable<Bar>> GetPastBars(Contract contract, BarLength barLength, int count)
        {
            return await GetHistoricalDataAsync(contract, barLength, default(DateTime), count);   
        }
   
        internal IEnumerable<Bar> GetPastBars(Contract contract, BarLength barLength, DateTime endDateTime, int count)
        {
            return GetHistoricalDataAsync(contract, barLength, endDateTime, count).Result;
        }

        public Task<LinkedList<MarketData.Bar>> GetHistoricalDataAsync(Contract contract, BarLength barLength, DateTime endDateTime, int count)
        {
            var reqId = NextRequestId;
            var tmpList = new LinkedList<MarketData.Bar>();

            var resolveResult = new TaskCompletionSource<LinkedList<MarketData.Bar>>();
            SetupHistoricalBarCallbacks(tmpList, reqId, barLength, resolveResult);

            //string timeFormat = "yyyyMMdd-HH:mm:ss";

            // Duration         : Allowed Bar Sizes
            // 60 S             : 1 sec - 1 mins
            // 120 S            : 1 sec - 2 mins
            // 1800 S (30 mins) : 1 sec - 30 mins
            // 3600 S (1 hr)    : 5 secs - 1 hr
            // 14400 S (4hr)	: 10 secs - 3 hrs
            // 28800 S (8 hrs)  : 30 secs - 8 hrs
            // 1 D              : 1 min - 1 day
            // 2 D              : 2 mins - 1 day
            // 1 W              : 3 mins - 1 week
            // 1 M              : 30 mins - 1 month
            // 1 Y              : 1 day - 1 month

            string durationStr = null;
            string barSizeStr = null;
            switch (barLength)
            {
                case BarLength._1Sec:
                    durationStr = $"{count} S";
                    barSizeStr = "1 secs";
                    break;

                case BarLength._5Sec:
                    durationStr = $"{5 * count} S";
                    barSizeStr = "5 secs";
                    break;

                case BarLength._1Min:
                    durationStr = $"{60 * count} S";
                    barSizeStr = "1 min";
                    break;

                default:
                    throw new NotImplementedException($"Unable to retrieve historical data for bar lenght {barLength}");
            }

            string edt = endDateTime == DateTime.MinValue ? String.Empty : $"{endDateTime.ToString("yyyyMMdd HH:mm:ss")} US/Eastern";

            _client.RequestHistoricalData(reqId, contract, edt, durationStr, barSizeStr, false);

            return resolveResult.Task;
        }

        private void SetupHistoricalBarCallbacks(LinkedList<MarketData.Bar> tmpList, int reqId, BarLength barLength, TaskCompletionSource<LinkedList<MarketData.Bar>> resolveResult)
        {
            var historicalData = new Action<int, MarketData.Bar>((rId, bar) =>
            {
                if (rId == reqId)
                {
                    _logger.Trace($"GetHistoricalDataAsync - historicalData - adding bar {bar.Time}");
                    bar.BarLength = barLength;
                    tmpList.AddLast(bar);
                }
            });

            var historicalDataEnd = new Action<int, string, string>((rId, start, end) =>
            {
                if (rId == reqId)
                {
                    _logger.Trace($"GetHistoricalDataAsync - historicalDataEnd - setting result");
                    resolveResult.SetResult(tmpList);
                }
            });

            _client.Callbacks.HistoricalData += historicalData;
            _client.Callbacks.HistoricalDataEnd += historicalDataEnd;

            resolveResult.Task.ContinueWith(t =>
            {
                _client.Callbacks.HistoricalData -= historicalData;
                _client.Callbacks.HistoricalDataEnd -= historicalDataEnd;
            });
        }

        public IEnumerable<BidAsk> GetPastBidAsks(Contract contract, DateTime time, int count)
        {
            return RequestHistoricalTicks(contract, time, count).Result;
        }

        public Task<IEnumerable<BidAsk>> RequestHistoricalTicks(Contract contract, DateTime time, int count)
        {
            var reqId = NextRequestId;
            var tmpList = new LinkedList<BidAsk>();

            var resolveResult = new TaskCompletionSource<IEnumerable<BidAsk>>();
            var historicalTicksBidAsk = new Action<int, IEnumerable<BidAsk>, bool>((rId, bas, isDone) =>
            {
                if (rId == reqId)
                {
                    _logger.Trace($"RequestHistoricalTicks - adding {bas.Count()} bidasks");

                    foreach (var ba in bas)
                        tmpList.AddLast(ba);

                    if (isDone)
                    {
                        _logger.Trace($"RequestHistoricalTicks - SetResult");
                        resolveResult.SetResult(tmpList);
                    }
                }
            });

            _client.Callbacks.HistoricalTicksBidAsk += historicalTicksBidAsk;
            resolveResult.Task.ContinueWith(t =>
            {
                _client.Callbacks.HistoricalTicksBidAsk -= historicalTicksBidAsk;
            });

            _client.RequestHistoricalTicks(reqId, contract, null, $"{time.ToString("yyyyMMdd HH:mm:ss")} US/Eastern", count, "BID_ASK", false, true);

            return resolveResult.Task;
        }

        public Task<DateTime> GetCurrentTimeAsync()
        {
            var tcs = new TaskCompletionSource<DateTime>();

            var currentTime = new Action<long>(time =>
            {
                tcs.SetResult(DateTimeOffset.FromUnixTimeSeconds(time).DateTime.ToLocalTime());
            });
            
            _client.Callbacks.CurrentTime += currentTime;
            tcs.Task.ContinueWith(task => _client.Callbacks.CurrentTime -= currentTime);

            _logger.Debug("Requesting current time");
            _client.RequestCurrentTime();
            return tcs.Task;
        }
    }
}
