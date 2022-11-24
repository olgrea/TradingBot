using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using InteractiveBrokers.Accounts;
using InteractiveBrokers.Backend;
using InteractiveBrokers.Contracts;
using InteractiveBrokers.MarketData;
using InteractiveBrokers.Messages;
using InteractiveBrokers.Orders;
using NLog;

[assembly: InternalsVisibleTo("Backtester")]
[assembly: InternalsVisibleTo("IBClient.Tests")]
[assembly: Fody.ConfigureAwait(false)]

namespace InteractiveBrokers
{
    internal class DataSubscriptions
    {
        public bool AccountUpdates { get; set; }
        public bool Positions { get; set; }

        //TODO : remove all that "by Contract" stuff?
        public Dictionary<Contract, int> BidAsk { get; set; } = new Dictionary<Contract, int>();
        public Dictionary<Contract, int> Last { get; set; } = new Dictionary<Contract, int>();
        public Dictionary<Contract, int> FiveSecBars { get; set; } = new Dictionary<Contract, int>();
        public Dictionary<Contract, int> Pnl { get; set; } = new Dictionary<Contract, int>();
    }

    public class IBClient
    {
        static Random rand = new Random();

        const int DefaultTWSPort = 7496;
        const int DefaultIBGatewayPort = 4002;
        const string DefaultIP = "127.0.0.1";

        int _port;
        int _clientId = 1337;
        int _reqId = 0;
        DataSubscriptions _subscriptions = new DataSubscriptions();
        IIBClientSocket _socket;
        ILogger _logger;
        Dictionary<Contract, LinkedList<MarketData.Bar>> _fiveSecBars = new Dictionary<Contract, LinkedList<MarketData.Bar>>();

        int NextRequestId => _reqId++;
        internal DataSubscriptions Subscriptions => _subscriptions;

        internal IIBClientSocket Socket => _socket;

        #region Events

        public Dictionary<BarLength, Action<Contract, Bar>> BarReceived { get; set; } = new Dictionary<BarLength, Action<Contract, Bar>>(
            Enum.GetValues(typeof(BarLength))
            .Cast<BarLength>()
            .Where(bl => bl != BarLength._1Sec) // Disable 1 sec event for now as TWS minimum resolution for realtime bars is 5 sec
            .Select(bl => new KeyValuePair<BarLength, Action<Contract, Bar>>(bl, null))
        );

        public event Action<Contract, BidAsk> BidAskReceived;
        public event Action<Contract, Last> LastReceived;

        public event Action<AccountValue> AccountValueUpdated
        {
            add => _socket.Callbacks.UpdateAccountValue += value;
            remove => _socket.Callbacks.UpdateAccountValue -= value;
        }

        public event Action<Position> PositionReceived
        {
            add => _socket.Callbacks.Position += value;
            remove => _socket.Callbacks.Position -= value;
        }

        public event Action<PnL> PnLReceived;

        public event Action<Contract, Order, OrderState> OrderOpened
        {
            add => _socket.Callbacks.OpenOrder += value;
            remove => _socket.Callbacks.OpenOrder -= value;
        }

        public event Action<OrderStatus> OrderStatusChanged
        {
            add => _socket.Callbacks.OrderStatus += value;
            remove => _socket.Callbacks.OrderStatus -= value;
        }

        public event Action<Contract, OrderExecution> OrderExecuted
        {
            add => _socket.Callbacks.ExecDetails += value;
            remove => _socket.Callbacks.ExecDetails -= value;
        }

        public event Action<CommissionInfo> CommissionInfoReceived
        {
            add => _socket.Callbacks.CommissionReport += value;
            remove => _socket.Callbacks.CommissionReport -= value;
        }

        public event Action<long> CurrentTimeReceived
        {
            add => _socket.Callbacks.CurrentTime += value;
            remove => _socket.Callbacks.CurrentTime -= value;
        }

        #endregion Events

        public IBClient()
        {
            Init(rand.Next(), null, null);
        }

        public IBClient(int clientId)
        {
            Init(clientId, null, null);
        }

        protected IBClient(int clientId, IIBClientSocket socket)
        {
            Init(clientId, socket, null);
        }

        void Init(int clientId, IIBClientSocket socket, ILogger logger)
        {
            _port = GetPort();
            _clientId = clientId;
            _logger = logger ?? LogManager.GetLogger($"{nameof(IBClient)}-{_clientId}");
            _socket = socket ?? new IBClientSocket(_logger);
        }

        int GetPort()
        {
            var ibGatewayProc = Process.GetProcessesByName("ibgateway").FirstOrDefault();
            if (ibGatewayProc != null)
                return DefaultIBGatewayPort;

            var twsProc = Process.GetProcessesByName("tws").FirstOrDefault();
            if (twsProc != null)
                return DefaultTWSPort;

            throw new ArgumentException("Neither TWS Workstation or IB Gateway is running.");
        }

        public async Task<ConnectResult> ConnectAsync()
        {
            return await ConnectAsync(TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 5000), CancellationToken.None);
        }

        public async Task<ConnectResult> ConnectAsync(TimeSpan timeout)
        {
            return await ConnectAsync(timeout, CancellationToken.None);
        }

        public async Task<ConnectResult> ConnectAsync(CancellationToken token)
        {
            return await ConnectAsync(TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 5000), token);
        }

        public async Task<ConnectResult> ConnectAsync(TimeSpan timeout, CancellationToken token)
        {
            //TODO: Handle IB server resets
            var result = new ConnectResult();
            var tcs = new TaskCompletionSource<ConnectResult>();
            token.Register(() => tcs.TrySetCanceled());

            var nextValidId = new Action<int>(id =>
            {
                _logger.Trace($"ConnectAsync : next valid id {id}");
                result.NextValidOrderId = id;

                if (result.IsSet())
                    tcs.TrySetResult(result);
            });

            var managedAccounts = new Action<IEnumerable<string>>(accList =>
            {
                if (accList.Count() > 1)
                    tcs.SetException(new NotSupportedException("Only single account structures are supported."));

                _logger.Trace($"ConnectAsync : managedAccounts {accList} - set result");
                result.AccountCode = accList.First();

                if (result.IsSet())
                    tcs.TrySetResult(result);
            });

            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _socket.Callbacks.NextValidId += nextValidId;
            _socket.Callbacks.ManagedAccounts += managedAccounts;
            _socket.Callbacks.Error += error;

            try
            {
                _ = Task.Delay(timeout, token).ContinueWith(t => tcs.TrySetException(new TimeoutException($"{nameof(ConnectAsync)}")));
                _socket.Connect(DefaultIP, _port, _clientId);
                await tcs.Task;

            }
            finally
            {
                _socket.Callbacks.NextValidId -= nextValidId;
                _socket.Callbacks.ManagedAccounts -= managedAccounts;
                _socket.Callbacks.Error -= error;
            }

            return tcs.Task.Result;
        }

        public async Task<bool> DisconnectAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            _logger.Debug($"Disconnecting from TWS");

            var disconnect = new Action(() => tcs.TrySetResult(true));
            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _socket.Callbacks.ConnectionClosed += disconnect;
            _socket.Callbacks.Error += error;
            try
            {
                _socket.Disconnect();
                return await tcs.Task;
            }
            finally
            {
                _socket.Callbacks.ConnectionClosed -= disconnect;
                _socket.Callbacks.Error -= error;
            }
        }

        public async Task<IEnumerable<string>> GetManagedAccountsList()
        {
            var tcs = new TaskCompletionSource<IEnumerable<string>>();

            var managedAccount = new Action<IEnumerable<string>>(list => tcs.SetResult(list));
            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _socket.Callbacks.ManagedAccounts += managedAccount;
            try
            {
                _socket.RequestManagedAccounts();
                return await tcs.Task;
            }
            finally
            {
                _socket.Callbacks.ManagedAccounts -= managedAccount;
                _socket.Callbacks.Error -= error;
            }
        }

        public async Task<int> GetNextValidOrderIdAsync()
        {
            CancellationTokenSource source = new CancellationTokenSource(Debugger.IsAttached ? -1 : 5000);
            return await GetNextValidOrderIdAsync(source.Token);
        }

        public async Task<int> GetNextValidOrderIdAsync(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<int>();
            token.Register(() => tcs.TrySetException(new TimeoutException($"{nameof(GetNextValidOrderIdAsync)}")));

            var nextValidId = new Action<int>(id => tcs.SetResult(id));
            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _socket.Callbacks.NextValidId += nextValidId;
            _socket.Callbacks.Error += error;
            try
            {
                _logger.Debug($"Requesting next valid order ids");
                _socket.RequestValidOrderIds();
                return await tcs.Task;
            }
            finally
            {
                _socket.Callbacks.NextValidId -= nextValidId;
                _socket.Callbacks.Error -= error;
            }
        }

        // In a single account structure, the account number is ignored.
        public async Task<Account> GetAccountAsync(string accountCode = null)
        {
            var accList = await GetManagedAccountsList();
            if(accList.Count() == 1)
            {
                accountCode ??= accList.First();
            }
            else if(string.IsNullOrEmpty(accountCode))
            {
                throw new ArgumentException("A non-null account code must be specified for multiple-account structures.");
            }
            else if(!accList.Contains(accountCode))
            {
                throw new ArgumentException($"The account code \"{accountCode}\" doesn't exists.");
            }

            var account = new Account();
            var tcs = new TaskCompletionSource<Account>();

            var updateAccountTime = new Action<TimeSpan>(time =>
            {
                _logger.Trace($"GetAccountAsync updateAccountTime : {time}");
                account.Time = time;
            });
            var updateAccountValue = new Action<AccountValue>(accValue =>
            {
                _logger.Trace($"GetAccountAsync updateAccountValue : key={accValue.Key}, value={accValue.Value}");
                switch (accValue.Key)
                {
                    case "AccountReady":
                        if (!bool.Parse(accValue.Value))
                        {
                            string msg = "Account not available at the moment. The IB server is in the process of resetting. Values returned may not be accurate.";
                            _logger.Warn(msg);
                        }
                        break;

                    case "CashBalance":
                        account.CashBalances[accValue.Currency] = double.Parse(accValue.Value, CultureInfo.InvariantCulture);
                        break;

                    case "RealizedPnL":
                        account.RealizedPnL[accValue.Currency] = double.Parse(accValue.Value, CultureInfo.InvariantCulture);
                        break;

                    case "UnrealizedPnL":
                        account.UnrealizedPnL[accValue.Currency] = double.Parse(accValue.Value, CultureInfo.InvariantCulture);
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
                account.Code = accountCode;
                tcs.SetResult(account);
            });

            _socket.Callbacks.UpdateAccountTime += updateAccountTime;
            _socket.Callbacks.UpdateAccountValue += updateAccountValue;
            _socket.Callbacks.UpdatePortfolio += updatePortfolio;
            _socket.Callbacks.AccountDownloadEnd += accountDownloadEnd;

            try
            {
                _socket.RequestAccountUpdates(accountCode);
                return await tcs.Task;
            }
            finally
            {
                _socket.CancelAccountUpdates(accountCode);

                _socket.Callbacks.UpdateAccountTime -= updateAccountTime;
                _socket.Callbacks.UpdateAccountValue -= updateAccountValue;
                _socket.Callbacks.UpdatePortfolio -= updatePortfolio;
                _socket.Callbacks.AccountDownloadEnd -= accountDownloadEnd;
            }
        }

        public async Task<Contract> GetContractAsync(string symbol, string exchange = "SMART")
        {
            var sampleContract = new Stock()
            {
                Currency = "USD",
                Exchange = exchange,
                Symbol = symbol,
                SecType = "STK"
            };

            var contractDetails = await GetContractDetailsAsync(sampleContract);
            return contractDetails?.FirstOrDefault().Contract;
        }

        public async Task<List<ContractDetails>> GetContractDetailsAsync(Contract contract)
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

            _socket.Callbacks.ContractDetails += contractDetails;
            _socket.Callbacks.ContractDetailsEnd += contractDetailsEnd;
            _socket.Callbacks.Error += error;

            try
            {
                _logger.Debug($"Requesting contract details for {contract} (reqId={reqId})");
                _socket.RequestContractDetails(reqId, contract);
                return await tcs.Task;
            }
            finally
            {
                _socket.Callbacks.ContractDetails -= contractDetails;
                _socket.Callbacks.ContractDetailsEnd -= contractDetailsEnd;
                _socket.Callbacks.Error -= error;
            }
        }

        public async Task<BidAsk> GetLatestBidAskAsync(Contract contract)
        {
            var tcs = new TaskCompletionSource<BidAsk>();
            CancellationTokenSource source = new CancellationTokenSource();
            source.Token.Register(() => tcs.TrySetException(new TimeoutException($"{nameof(GetLatestBidAskAsync)}")));
            int timeoutInMs = Debugger.IsAttached ? -1 : 5000;

            var reqId = NextRequestId;

            var tickByTickBidAsk = new Action<int, BidAsk>((rId, ba) =>
            {
                if (reqId == rId)
                    tcs.TrySetResult(ba);
            });

            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _socket.Callbacks.TickByTickBidAsk += tickByTickBidAsk;
            _socket.Callbacks.Error += error;
            try
            {
                source.CancelAfter(timeoutInMs);
                _socket.RequestTickByTickData(reqId, contract, "BidAsk");
                return await tcs.Task;
            }
            finally
            {
                _socket.CancelTickByTickData(reqId);
                _socket.Callbacks.TickByTickBidAsk -= tickByTickBidAsk;
                _socket.Callbacks.Error -= error;
            }
        }

        public void RequestBidAskUpdates(Contract contract)
        {
            if (_subscriptions.BidAsk.ContainsKey(contract))
                return;

            int reqId = NextRequestId;
            _subscriptions.BidAsk[contract] = reqId;
            if (_subscriptions.BidAsk.Count == 1)
                _socket.Callbacks.TickByTickBidAsk += TickByTickBidAsk;

            _socket.RequestTickByTickData(reqId, contract, "BidAsk");
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
                _socket.Callbacks.TickByTickBidAsk -= TickByTickBidAsk;

            _socket.CancelTickByTickData(reqId);
        }

        public void RequestLastUpdates(Contract contract)
        {
            if (_subscriptions.Last.ContainsKey(contract))
                return;

            int reqId = NextRequestId;
            _subscriptions.Last[contract] = reqId;
            if (_subscriptions.Last.Count == 1)
                _socket.Callbacks.TickByTickAllLast += TickByTickLast;

            _socket.RequestTickByTickData(reqId, contract, "Last");
        }

        void TickByTickLast(int reqId, Last last)
        {
            var contract = _subscriptions.Last.First(c => c.Value == reqId).Key;
            LastReceived?.Invoke(contract, last);
        }

        public void CancelLastUpdates(Contract contract)
        {
            if (!_subscriptions.Last.ContainsKey(contract))
                return;

            var reqId = _subscriptions.Last[contract];
            _subscriptions.Last.Remove(contract);
            if (_subscriptions.Last.Count == 0)
                _socket.Callbacks.TickByTickAllLast -= TickByTickLast;

            _socket.CancelTickByTickData(reqId);
        }

        public void RequestBarsUpdates(Contract contract)
        {
            if (_subscriptions.FiveSecBars.ContainsKey(contract))
                return;

            int reqId = NextRequestId;
            _subscriptions.FiveSecBars[contract] = reqId;
            _fiveSecBars[contract] = new LinkedList<Bar>();
            if (_subscriptions.FiveSecBars.Count == 1)
                _socket.Callbacks.RealtimeBar += OnFiveSecondsBarReceived;

            _socket.RequestFiveSecondsBarUpdates(reqId, contract);
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
            // arbitrarily keeping 5 minutes of bars
            if (list.Count > 60)
                list.RemoveFirst();

            foreach (BarLength barLength in Enum.GetValues(typeof(BarLength)).OfType<BarLength>())
            {
                if (!HasSubscribers(barLength))
                    continue;

                int sec = (int)barLength;
                int nbBars = sec / 5;
                if (list.Count >= nbBars && (bar.Time.Second + 5) % sec == 0)
                {
                    Bar barToUse = bar;
                    if (barLength > BarLength._5Sec)
                    {
                        barToUse = MarketDataUtils.CombineBars(list.TakeLast(nbBars), barLength);
                    }

                    InvokeCallbacks(contract, barToUse);
                }
            }
        }

        void InvokeCallbacks(Contract contract, Bar bar)
        {
            _logger.Trace($"Invoking {bar.BarLength} callback for {contract}");
            BarReceived[bar.BarLength]?.Invoke(contract, bar);
        }

        bool HasSubscribers(BarLength barLength)
        {
            return BarReceived.ContainsKey(barLength) && BarReceived[barLength] != null;
        }

        public void CancelBarsUpdates(Contract contract)
        {
            if (!_subscriptions.FiveSecBars.ContainsKey(contract))
                return;

            var reqId = _subscriptions.FiveSecBars[contract];
            _subscriptions.FiveSecBars.Remove(contract);
            _fiveSecBars.Remove(contract);

            if (_subscriptions.FiveSecBars.Count == 0)
                _socket.Callbacks.RealtimeBar -= OnFiveSecondsBarReceived;

            _socket.CancelFiveSecondsBarsUpdates(reqId);
        }

        public async Task<OrderResult> PlaceOrderAsync(Contract contract, Order order)
        {
            var source = new CancellationTokenSource(Debugger.IsAttached ? -1 : 5000);
            return await PlaceOrderAsync(contract, order, source.Token);
        }

        public async Task<OrderResult> PlaceOrderAsync(Contract contract, Order order, CancellationToken token)
        {
            if (order?.Id <= 0)
                throw new ArgumentException("Order id not set");

            var orderPlacedResult = new OrderPlacedResult();
            var tcs = new TaskCompletionSource<OrderResult>();
            token.Register(() => tcs.TrySetException(new TimeoutException($"{nameof(PlaceOrderAsync)}")));

            var openOrder = new Action<Contract, Order, OrderState>((c, o, oState) =>
            {
                if (order.Id == o.Id)
                {
                    orderPlacedResult.Contract = c;
                    orderPlacedResult.Order = o;
                    orderPlacedResult.OrderState = oState;
                }
            });

            // With market orders, when the order is accepted and executes immediately, there commonly will not be any
            // corresponding orderStatus callbacks. For that reason it is recommended to also monitor the IBApi.EWrapper.execDetails .
            // TODO : Wasn't able to reproduce this behavior with tests? Potential relevant discussion here : 
            // https://groups.io/g/twsapi/topic/trading_in_the_last_minute_of/79443776?p=,,,20,0,0,0::recentpostdate%2Fsticky,,,20,2,0,79443776
            var orderStatus = new Action<OrderStatus>(oStatus =>
            {
                if (order.Id == oStatus.Info.OrderId && (oStatus.Status == Status.PreSubmitted || oStatus.Status == Status.Submitted))
                {
                    orderPlacedResult.OrderStatus = oStatus;
                    tcs.TrySetResult(orderPlacedResult);
                }
            });

            var error = new Action<ErrorMessageException>(msg =>
            {
                if (!MarketDataUtils.IsMarketOpen() && msg.ErrorCode == 399 && msg.Message.Contains("your order will not be placed at the exchange until"))
                    return;

                tcs.TrySetException(msg);
            });

            _socket.Callbacks.OpenOrder += openOrder;
            _socket.Callbacks.OrderStatus += orderStatus;
            _socket.Callbacks.Error += error;

            try
            {
                _socket.PlaceOrder(contract, order);
                return await tcs.Task;
            }
            finally
            {
                _socket.Callbacks.OpenOrder -= openOrder;
                _socket.Callbacks.OrderStatus -= orderStatus;
                _socket.Callbacks.Error -= error;
            }
        }

        public void PlaceOrder(Contract contract, Order order)
        {
            if (order?.Id <= 0)
                throw new ArgumentException("Order id not set");

            if (!order.Info.Transmit)
                _logger.Warn($"Order will not be submitted automatically since \"{nameof(order.Info.Transmit)}\" is set to false.");

            _socket.PlaceOrder(contract, order);
        }

        public async Task<OrderStatus> CancelOrderAsync(int orderId)
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

            _socket.Callbacks.OrderStatus += orderStatus;
            _socket.Callbacks.Error += error;
            try
            {
                _logger.Debug($"Requesting order cancellation for order id : {orderId}");

                source.CancelAfter(timeoutInMs);
                _socket.CancelOrder(orderId);

                return await tcs.Task;
            }
            finally
            {
                _socket.Callbacks.OrderStatus -= orderStatus;
                _socket.Callbacks.Error -= error;
            }
        }

        public void CancelOrder(Order order)
        {
            Trace.Assert(order.Id > 0);
            _socket.CancelOrder(order.Id);
        }

        public void CancelAllOrders()
        {
            _socket.CancelAllOrders();
        }

        public void RequestPositionsUpdates()
        {
            _socket.RequestPositionsUpdates();
            _subscriptions.Positions = true;
        }

        public void CancelPositionsUpdates()
        {
            _subscriptions.Positions = false;
            _socket.CancelPositionsUpdates();
        }

        public void RequestPnLUpdates(Contract contract)
        {
            if (_subscriptions.Pnl.ContainsKey(contract))
                return;

            int reqId = NextRequestId;
            _subscriptions.Pnl[contract] = reqId;
            if (_subscriptions.Pnl.Count == 1)
                _socket.Callbacks.PnlSingle += PnlSingle;

            _socket.RequestPnLUpdates(reqId, contract.Id);
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
                _socket.Callbacks.PnlSingle -= PnlSingle;

            _socket.CancelPnLUpdates(_subscriptions.Pnl[contract]);
        }

        public void RequestAccountUpdates(string account)
        {
            _subscriptions.AccountUpdates = true;
            _socket.RequestAccountUpdates(account);
        }

        public void CancelAccountUpdates(string account)
        {
            if (_subscriptions.AccountUpdates)
            {
                _socket.CancelAccountUpdates(account);
            }
        }

        public Task<IEnumerable<Bar>> GetHistoricalBarsAsync(Contract contract, BarLength barLength, DateTime endDateTime, int count)
        {
            var reqId = NextRequestId;
            var tmpList = new LinkedList<Bar>();

            var resolveResult = new TaskCompletionSource<IEnumerable<Bar>>();
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

            string edt = endDateTime == DateTime.MinValue ? string.Empty : $"{endDateTime.ToString("yyyyMMdd HH:mm:ss")} US/Eastern";

            _socket.RequestHistoricalData(reqId, contract, edt, durationStr, barSizeStr, true);

            return resolveResult.Task;
        }

        private void SetupHistoricalBarCallbacks(LinkedList<MarketData.Bar> tmpList, int reqId, BarLength barLength, TaskCompletionSource<IEnumerable<Bar>> resolveResult)
        {
            var historicalData = new Action<int, Bar>((rId, bar) =>
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

            _socket.Callbacks.HistoricalData += historicalData;
            _socket.Callbacks.HistoricalDataEnd += historicalDataEnd;

            resolveResult.Task.ContinueWith(t =>
            {
                _socket.Callbacks.HistoricalData -= historicalData;
                _socket.Callbacks.HistoricalDataEnd -= historicalDataEnd;
            });
        }

        public async Task<IEnumerable<BidAsk>> GetHistoricalBidAsksAsync(Contract contract, DateTime time, int count)
        {
            CancellationTokenSource source = new CancellationTokenSource(Debugger.IsAttached ? -1 : 5000);
            return await GetHistoricalTicksAsync<BidAsk>(contract, time, count, "BID_ASK", source.Token);
        }
        public async Task<IEnumerable<Last>> GetHistoricalLastsAsync(Contract contract, DateTime time, int count)
        {
            CancellationTokenSource source = new CancellationTokenSource(Debugger.IsAttached ? -1 : 5000);
            return await GetHistoricalTicksAsync<Last>(contract, time, count, "TRADES", source.Token);
        }

        async Task<IEnumerable<TData>> GetHistoricalTicksAsync<TData>(Contract contract, DateTime time, int count, string whatToShow, CancellationToken token) where TData : IMarketData, new()
        {
            var reqId = NextRequestId;
            var tmpList = new LinkedList<TData>();

            var tcs = new TaskCompletionSource<IEnumerable<TData>>();
            token.Register(() => tcs.TrySetException(new TimeoutException($"GetHistorical{typeof(TData).Name}Async")));
            var historicalTicks = new Action<int, IEnumerable<TData>, bool>((rId, data, isDone) =>
            {
                if (rId == reqId)
                {
                    _logger.Trace($"RequestHistoricalTicks {typeof(TData).Name} - adding {data.Count()}");

                    foreach (var ba in data)
                        tmpList.AddLast(ba);

                    if (isDone)
                    {
                        _logger.Trace($"RequestHistoricalTicks {typeof(TData).Name} - SetResult");
                        tcs.SetResult(tmpList);
                    }
                }
            });

            Action unregisterAction;
            if (typeof(TData) == typeof(BidAsk))
            {
                Action<int, IEnumerable<BidAsk>, bool> callback = (Action<int, IEnumerable<BidAsk>, bool>)historicalTicks;

                _socket.Callbacks.HistoricalTicksBidAsk += callback;
                unregisterAction = new Action(() => _socket.Callbacks.HistoricalTicksBidAsk -= callback);
            }
            else if (typeof(TData) == typeof(Last))
            {
                Action<int, IEnumerable<Last>, bool> callback = (Action<int, IEnumerable<Last>, bool>)historicalTicks;

                _socket.Callbacks.HistoricalTicksLast += callback;
                unregisterAction = new Action(() => _socket.Callbacks.HistoricalTicksLast -= callback);
            }
            else
                throw new NotImplementedException();

            try
            {
                _socket.RequestHistoricalTicks(reqId, contract, null, $"{time.ToString("yyyyMMdd HH:mm:ss")} US/Eastern", count, whatToShow, false, true);
                return await tcs.Task;
            }
            finally
            {
                unregisterAction.Invoke();
            }

        }

        public async Task<DateTime> GetCurrentTimeAsync()
        {
            var tcs = new TaskCompletionSource<DateTime>();

            var currentTime = new Action<long>(time =>
            {
                tcs.SetResult(DateTimeOffset.FromUnixTimeSeconds(time).DateTime.ToLocalTime());
            });

            _socket.Callbacks.CurrentTime += currentTime;
            try
            {
                _logger.Debug("Requesting current time");
                _socket.RequestCurrentTime();
                return await tcs.Task;
            }
            finally
            {
                _socket.Callbacks.CurrentTime -= currentTime;
            }
        }

        public virtual async Task WaitUntil(TimeSpan endTime, IProgress<TimeSpan> progress, CancellationToken token)
        {
            TimeSpan currentTime = (await GetCurrentTimeAsync()).TimeOfDay;
            progress?.Report(currentTime);

            while (!token.IsCancellationRequested && currentTime < endTime)
            {
                await Task.Delay(500);
                currentTime = (await GetCurrentTimeAsync()).TimeOfDay;
                progress?.Report(currentTime);
            }
        }
    }
}
