using System;
using System.Linq;
using System.Collections.Generic;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Utils;
using System.Threading.Tasks;
using System.Globalization;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleToAttribute("HistoricalDataFetcher")]
namespace TradingBot.Broker
{
    internal class DataSubscriptions
    {
        public bool AccountUpdates { get; set; }
        public bool Positions { get; set; }
        public Dictionary<Contract, int> BidAsk { get; set; } = new Dictionary<Contract, int>();
        public Dictionary<Contract, int> FiveSecBars { get; set; } = new Dictionary<Contract, int>();
        public Dictionary<Contract, int> Pnl { get; set; } = new Dictionary<Contract, int>();
    }

    internal class IBBroker : IBroker
    {
        static HashSet<int> _clientIds = new HashSet<int>();

        const int DefaultPort = 7496;
        const string DefaultIP = "127.0.0.1";
        
        int _clientId = 1337;
        int _nextValidOrderId = -1;
        int _reqId = 0;
        string _accountCode;

        DataSubscriptions _subscriptions;
        IIBClient _client;
        ILogger _logger;
        OrderManager _orderManager;
        Dictionary<Contract, LinkedList<MarketData.Bar>> _fiveSecBars = new Dictionary<Contract, LinkedList<MarketData.Bar>>();

        int NextRequestId => _reqId++;
        int NextValidOrderId => _nextValidOrderId++;

        public IBBroker(int clientId, ILogger logger)
        {
            if (_clientIds.Contains(clientId))
                throw new ArgumentException($"The client id {clientId} is already assigned.");

            _clientId = clientId;
            _clientIds.Add(clientId);
            
            _subscriptions = new DataSubscriptions();
            _client = new IBClient(logger);
            _client.Callbacks.TickByTickBidAsk += TickByTickBidAsk;
            _client.Callbacks.PnlSingle += PnlSingle;
            _client.Callbacks.RealtimeBar += OnFiveSecondsBarReceived;

            _logger = logger;

            _orderManager = new OrderManager(this, _client, _logger);
        }

        public event Action ClientConnected;
        public event Action ClientDisconnected;
        public event Action<Contract, Bar> Bar5SecReceived;
        public event Action<Contract, Bar> Bar1MinReceived;
        public event Action<Contract, BidAsk> BidAskReceived;

        public event Action<string, string, string> AccountValueUpdated
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
        public event Action<ClientMessage> ClientMessageReceived
        {
            add => _client.Callbacks.Message += value;
            remove => _client.Callbacks.Message -= value;
        }
        public event Action<Contract, Order, OrderState> OrderOpened
        {
            add => _client.Callbacks.OpenOrder += value;
            remove => _client.Callbacks.OpenOrder -= value;
        }
        public event Action<OrderStatus> OrderStatusChanged
        {
            add => _client.Callbacks.OrderStatus += value;
            remove => _client.Callbacks.OrderStatus -= value;
        }
        public event Action<Contract, OrderExecution> OrderExecuted
        {
            add => _client.Callbacks.ExecDetails += value;
            remove => _client.Callbacks.ExecDetails -= value;
        }
        public event Action<CommissionInfo> CommissionInfoReceived
        {
            add => _client.Callbacks.CommissionReport += value;
            remove => _client.Callbacks.CommissionReport -= value;
        }

        public int GetNextValidOrderId(bool fromTWS = false)
        {
            if (fromTWS)
            {
                return GetNextValidOrderIdAsync().Result;
            }
            return NextValidOrderId;
        }

        Task<int> GetNextValidOrderIdAsync()
        {
            var resolveResult = new TaskCompletionSource<int>();
            var nextValidId = new Action<int>(id => resolveResult.SetResult(id));
            var error = new Action<ClientMessage>(msg => TaskError(msg, resolveResult));

            _client.Callbacks.NextValidId += nextValidId;
            _client.Callbacks.Message += error;
            resolveResult.Task.ContinueWith(t =>
            {
                _client.Callbacks.NextValidId -= nextValidId;
                _client.Callbacks.Message -= error;
            });

            _client.RequestValidOrderIds();

            return resolveResult.Task;
        }

        public void Connect()
        {
            ConnectAsync(DefaultIP, DefaultPort, _clientId).Wait();
        }

        Task<bool> ConnectAsync(string host, int port, int clientId)
        {
            //TODO: Handle IB server resets

            var resolveResult = new TaskCompletionSource<bool>();
            var nextValidId = new Action<int>(id =>
            {
                if (_nextValidOrderId < 0)
                {
                    _logger.LogInfo($"Client connected.");
                }
                _nextValidOrderId = id;
            });

            var managedAccounts = new Action<string>(acc =>
            {
                _accountCode = acc;
                resolveResult.SetResult(_nextValidOrderId > 0 && !string.IsNullOrEmpty(_accountCode));
            });

            var error = new Action<ClientMessage>(msg => TaskError(msg, resolveResult));

            _client.Callbacks.NextValidId += nextValidId;
            _client.Callbacks.ManagedAccounts += managedAccounts;
            _client.Callbacks.Message += error;
            resolveResult.Task.ContinueWith(t =>
            {
                _client.Callbacks.NextValidId -= nextValidId;
                _client.Callbacks.ManagedAccounts -= managedAccounts;
                _client.Callbacks.Message -= error;

                if (_nextValidOrderId > 0)
                    ClientConnected?.Invoke();
            });

            _client.Connect(host, port, clientId);

            return resolveResult.Task;
        }

        public void Disconnect()
        {
            _client.Disconnect();
            ClientDisconnected?.Invoke();
        }

        public Accounts.Account GetAccount()
        {
            return GetAccountAsync().Result;
        }

        Task<Account> GetAccountAsync(bool receiveUpdates = true)
        {
            var account = new Account() { Code = _accountCode };

            var resolveResult = new TaskCompletionSource<Account>();

            var updateAccountTime = new Action<string>(time => account.Time = DateTime.Parse(time, CultureInfo.InvariantCulture));
            var updateAccountValue = new Action<string, string, string>((key, value, currency) =>
            {
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
            var updatePortfolio = new Action<Position>(pos => account.Positions.Add(pos));
            var accountDownloadEnd = new Action<string>(accountCode => resolveResult.SetResult(account));
            var error = new Action<ClientMessage>(msg => TaskError(msg, resolveResult));

            _client.Callbacks.UpdateAccountTime += updateAccountTime;
            _client.Callbacks.UpdateAccountValue += updateAccountValue;
            _client.Callbacks.UpdatePortfolio += updatePortfolio;
            _client.Callbacks.AccountDownloadEnd += accountDownloadEnd;
            _client.Callbacks.Message += error;

            resolveResult.Task.ContinueWith(t =>
            {
                _client.Callbacks.UpdateAccountTime -= updateAccountTime;
                _client.Callbacks.UpdateAccountValue -= updateAccountValue;
                _client.Callbacks.UpdatePortfolio -= updatePortfolio;
                _client.Callbacks.AccountDownloadEnd -= accountDownloadEnd;
                _client.Callbacks.Message -= error;

                if (!receiveUpdates)
                    _client.RequestAccount(_accountCode, false);
            });

            _client.RequestAccount(_accountCode, true);

            return resolveResult.Task;
        }

        public Contract GetContract(string ticker)
        {
            return GetContractsAsync(ticker).Result?.FirstOrDefault();
        }

        Task<List<Contract>> GetContractsAsync(string ticker)
        {
            var sampleContract = new Stock()
            {
                Currency = "USD",
                Exchange = "SMART",
                Symbol = ticker,
                SecType = "STK"
            };

            var reqId = NextRequestId;

            var resolveResult = new TaskCompletionSource<List<Contract>>();
            var error = new Action<ClientMessage>(msg => TaskError(msg, resolveResult));
            var tmpContracts = new List<Contract>();
            var contractDetails = new Action<int, Contract>((rId, c) =>
            {
                if (rId == reqId)
                    tmpContracts.Add(c);
            });
            var contractDetailsEnd = new Action<int>(rId =>
            {
                if (rId == reqId)
                    resolveResult.SetResult(tmpContracts);
            });

            _client.Callbacks.ContractDetails += contractDetails;
            _client.Callbacks.ContractDetailsEnd += contractDetailsEnd;
            _client.Callbacks.Message += error;

            resolveResult.Task.ContinueWith(t =>
            {
                _client.Callbacks.ContractDetails -= contractDetails;
                _client.Callbacks.ContractDetailsEnd -= contractDetailsEnd;
                _client.Callbacks.Message -= error;
            });

            _client.RequestContract(reqId, sampleContract);

            return resolveResult.Task;
        }

        public void RequestBidAsk(Contract contract)
        {
            if (_subscriptions.BidAsk.ContainsKey(contract))
                return;

            int reqId = NextRequestId;
            _subscriptions.BidAsk[contract] = reqId;

            _client.RequestTickByTickData(reqId, contract, "BidAsk");
        }

        public void CancelBidAskRequest(Contract contract)
        {
            if (_subscriptions.BidAsk.ContainsKey(contract))
            {
                _client.CancelTickByTickData(_subscriptions.BidAsk[contract]);
                _subscriptions.BidAsk.Remove(contract);
            }
        }

        public void RequestBars(Contract contract, BarLength barLength)
        {
            if (_subscriptions.FiveSecBars.ContainsKey(contract))
                return;

            int reqId = NextRequestId;
            _subscriptions.FiveSecBars[contract] = reqId;

            // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
            _client.RequestFiveSecondsBars(reqId, contract);
        }

        void OnFiveSecondsBarReceived(int reqId, MarketData.Bar bar)
        {
            var contract = _subscriptions.FiveSecBars.First(c => c.Value == reqId).Key;

            if (!_fiveSecBars.ContainsKey(contract))
            {
                _fiveSecBars.Add(contract, new LinkedList<MarketData.Bar>());
            }

            var list = _fiveSecBars[contract];
            list.AddFirst(bar);
            // keeping 5 minutes of bars
            if (list.Count > 60)
            {
                list.RemoveLast();
            }

            foreach(BarLength barLength in Enum.GetValues(typeof(BarLength)).OfType<BarLength>())
            {
                if (!HasSubscribers(barLength))
                    return;

                if(barLength == BarLength._5Sec)
                {
                    Bar5SecReceived?.Invoke(contract, bar);
                    return;
                }

                int sec = (int)barLength;
                if (list.Count > (sec / 5) + 1 && (bar.Time.Second % sec) == 0)
                {
                    var newBar = BarsUtils.MakeBar(list, barLength);
                    InvokeCallbacks(contract, newBar);
                }
            }
        }

        void InvokeCallbacks(Contract contract, MarketData.Bar bar)
        {
            switch (bar.BarLength)
            {
                case BarLength._5Sec: Bar5SecReceived?.Invoke(contract, bar); break;
                case BarLength._1Min: Bar1MinReceived?.Invoke(contract, bar); break;
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

        //MarketData.Bar MakeBar(LinkedList<MarketData.Bar> list, int seconds)
        //{
        //    MarketData.Bar bar = new MarketData.Bar() { High = double.MinValue, Low = double.MaxValue};
        //    var e = list.GetEnumerator();
        //    e.MoveNext();

        //    // The 1st bar shouldn't be included.
        //    e.MoveNext();

        //    int nbBars = seconds / 5;
        //    for (int i = 0; i < nbBars; i++, e.MoveNext())
        //    {
        //        MarketData.Bar current = e.Current;
        //        if(i == 0)
        //        {
        //            bar.Close = current.Close;
        //        }

        //        bar.High = Math.Max(bar.High, current.High);
        //        bar.Low = Math.Min(bar.Low, current.Low);
        //        bar.Volume += current.Volume;
        //        bar.TradeAmount += current.TradeAmount;

        //        if (i == nbBars - 1)
        //        {
        //            bar.Open = current.Open;
        //            bar.Time = current.Time;
        //        }
        //    }

        //    return bar;
        //}

        public void CancelBarsRequest(Contract contract, BarLength barLength)
        {
            if (!HasSubscribers(barLength))
                return;

            var allBarLengths = Enum.GetValues(typeof(BarLength)).OfType<BarLength>();
            if (allBarLengths.All(b => !HasSubscribers(b)))
            {
                var reqId = _subscriptions.FiveSecBars.First(kvp => kvp.Key == contract).Value;
                _client.CancelFiveSecondsBarsRequest(reqId);
                _fiveSecBars.Remove(contract);
            }
        }

        public void PlaceOrder(Contract contract, Order order)
        {
            _orderManager.PlaceOrder(contract, order);
        }

        public void PlaceOrder(Contract contract, OrderChain chain, bool useTWSAttachedOrderFeature = false)
        {
            _orderManager.PlaceOrder(contract, chain, useTWSAttachedOrderFeature);
        }

        public void ModifyOrder(Contract contract, Order order)
        {
            _orderManager.ModifyOrder(contract, order);
        }

        public void CancelOrder(Order order)
        {
            _orderManager.CancelOrder(order);
        }

        public void RequestPositions()
        {
            _client.RequestPositions();
            _subscriptions.Positions = true;
        }

        public void CancelPositionsSubscription()
        {
            _subscriptions.Positions = false;
            _client.CancelPositions();
        }

        public void RequestPnL(Contract contract)
        {
            if (_subscriptions.Pnl.ContainsKey(contract))
                return;

            int reqId = NextRequestId;
            _subscriptions.Pnl[contract] = reqId;
            _client.RequestPnL(reqId, _accountCode, contract.Id);
        }

        public void CancelPnLSubscription(Contract contract)
        {
            if (_subscriptions.Pnl.ContainsKey(contract))
            {
                _client.CancelPnL(_subscriptions.Pnl[contract]);
                _subscriptions.Pnl.Remove(contract);
            }
        }

        public LinkedList<Bar> GetPastBars(Contract contract, BarLength barLength, int count)
        {
            return GetHistoricalDataAsync(contract, barLength, string.Empty, count).Result;   
        }

        public Task<LinkedList<MarketData.Bar>> GetHistoricalDataAsync(Contract contract, BarLength barLength, string endDateTime, int count)
        {
            var tmpList = new LinkedList<MarketData.Bar>();
            var reqId = NextRequestId;

            var resolveResult = new TaskCompletionSource<LinkedList<MarketData.Bar>>();
            SetupHistoricalDataCallbacks(tmpList, reqId, barLength, resolveResult);

            //string timeFormat = "yyyyMMdd-HH:mm:ss";
            string durationStr = null;
            string barSizeStr = null;
            switch (barLength)
            {
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

            _client.RequestHistoricalData(reqId, contract, endDateTime, durationStr, barSizeStr, false);

            return resolveResult.Task;
        }

        private void SetupHistoricalDataCallbacks(LinkedList<MarketData.Bar> tmpList, int reqId, BarLength barLength, TaskCompletionSource<LinkedList<MarketData.Bar>> resolveResult)
        {
            var historicalData = new Action<int, MarketData.Bar>((rId, bar) =>
            {
                if (rId == reqId)
                {
                    bar.BarLength = barLength;
                    tmpList.AddLast(bar);
                }
            });

            var historicalDataEnd = new Action<int, string, string>((rId, start, end) =>
            {
                if (rId == reqId)
                {
                    resolveResult.SetResult(tmpList);
                }
            });

            var error = new Action<ClientMessage>(msg => TaskError(msg, resolveResult));

            _client.Callbacks.HistoricalData += historicalData;
            _client.Callbacks.HistoricalDataEnd += historicalDataEnd;
            _client.Callbacks.Message += error;

            resolveResult.Task.ContinueWith(t =>
            {
                _client.Callbacks.HistoricalData -= historicalData;
                _client.Callbacks.HistoricalDataEnd -= historicalDataEnd;
                _client.Callbacks.Message -= error;
            });
        }

        void TaskError<T>(ClientMessage msg, TaskCompletionSource<T> resolveResult)
        {
            if (msg is ClientError)
            {
                if (msg is ClientException ex)
                    resolveResult.SetException(ex.Exception);
                resolveResult.SetResult(default(T));
            }
        }

        void TickByTickBidAsk(int reqId, BidAsk bidAsk)
        {
            var contract = _subscriptions.BidAsk.First(c => c.Value == reqId).Key;
            BidAskReceived?.Invoke(contract, bidAsk);
        }

        void PnlSingle(int reqId, PnL pnl)
        {
            pnl.Contract = _subscriptions.Pnl.First(s => s.Value == reqId).Key;
            PnLReceived?.Invoke(pnl);
        }
    }
}
