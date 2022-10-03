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

[assembly: InternalsVisibleToAttribute("HistoricalDataFetcher")]
[assembly: InternalsVisibleToAttribute("Tests")]
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
        public const int DefaultPort = 7496;
        public const string DefaultIP = "127.0.0.1";

        static HashSet<int> _clientIds = new HashSet<int>();
        static Random rand = new Random();
        
        int _clientId = 1337;
        int _reqId = 0;
        
        DataSubscriptions _subscriptions;
        IIBClient _client;
        ILogger _logger;
        OrderManager _orderManager;
        Dictionary<Contract, LinkedList<MarketData.Bar>> _fiveSecBars = new Dictionary<Contract, LinkedList<MarketData.Bar>>();

        int NextRequestId => _reqId++;

        public IBBroker()
        {
            var clientId = rand.Next();
            if (!IsValid(clientId))
                throw new ArgumentException($"The client id {clientId} is already assigned.");

            _logger = LogManager.GetLogger($"{nameof(IBBroker)}-{_clientId}");
            _client = new IBClient(_logger);
            Init(_client, _logger);
        }

        public IBBroker(int clientId)
        {
            if (!IsValid(clientId))
                throw new ArgumentException($"The client id {clientId} is already assigned.");
            
            _logger = LogManager.GetLogger($"{nameof(IBBroker)}-{_clientId}");
            _client = new IBClient(_logger);
            Init(_client, _logger);
        }

        internal IBBroker(int clientId, IIBClient client)
        {
            if (!IsValid(clientId))
                throw new ArgumentException($"The client id {clientId} is already assigned.");

            _logger = LogManager.GetLogger($"{nameof(IBBroker)}-{_clientId}");
            _client = client;
            Init(_client, _logger);
        }

        bool IsValid(int clientId)
        {
            if (_clientIds.Contains(clientId))
                return false;

            _clientId = clientId;
            _clientIds.Add(clientId);
            return true;
        }

        void Init(IIBClient client, ILogger logger)
        {
            _subscriptions = new DataSubscriptions();

            _client.Callbacks.TickByTickBidAsk += TickByTickBidAsk;
            _client.Callbacks.PnlSingle += PnlSingle;
            _client.Callbacks.RealtimeBar += OnFiveSecondsBarReceived;

            _orderManager = new OrderManager(this, _client, _logger);
        }

        internal DataSubscriptions Subscriptions => _subscriptions;

        public event Action ClientConnected;
        public event Action ClientDisconnected;

        // TODO : revert back to dictionary of Action<> ?
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

        public IErrorHandler ErrorHandler
        {
            get => _client.Callbacks.ErrorHandler;
            set => _client.Callbacks.ErrorHandler = value;
        }
        
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

        public bool HasBeenRequested(Order order) => _orderManager.HasBeenRequested(order);
        public bool HasBeenOpened(Order order) => _orderManager.HasBeenOpened(order);
        public bool IsExecuted(Order order) => _orderManager.IsExecuted(order); 

        public DateTime GetCurrentTime()
        {
            return DateTimeOffset.FromUnixTimeSeconds(_client.GetCurrentTime().Result).DateTime.ToLocalTime();
        }

        public int GetNextValidOrderId()
        {
            return GetNextValidOrderIdAsync().Result;
        }

        Task<int> GetNextValidOrderIdAsync()
        {
            var resolveResult = new TaskCompletionSource<int>();
            var nextValidId = new Action<int>(id => resolveResult.SetResult(id));

            _client.Callbacks.NextValidId += nextValidId;
            resolveResult.Task.ContinueWith(t =>
            {
                _client.Callbacks.NextValidId -= nextValidId;
            });

            _client.RequestValidOrderIds();

            return resolveResult.Task;
        }

        public void Connect()
        {
            _client.ConnectAsync(DefaultIP, DefaultPort, _clientId).Wait();
            ClientConnected?.Invoke();
        }
                
        public void Disconnect()
        {
            _client.Disconnect();
            ClientDisconnected?.Invoke();
        }

        public Account GetAccount()
        {
            return _client.GetAccountAsync().Result;
        }

        public Contract GetContract(string ticker)
        {
            var sampleContract = new Stock()
            {
                Currency = "USD",
                Exchange = "SMART",
                Symbol = ticker,
                SecType = "STK"
            };

            return _client.GetContractsAsync(NextRequestId, sampleContract).Result?.FirstOrDefault();
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

            _logger.Debug($"Requesting bar of length {barLength}");
            int reqId = NextRequestId;
            _subscriptions.FiveSecBars[contract] = reqId;

            // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
            _client.RequestFiveSecondsBars(reqId, contract);
        }

        void OnFiveSecondsBarReceived(int reqId, MarketData.Bar bar)
        {
            Trace.Assert(_subscriptions.FiveSecBars.ContainsValue(reqId));
            
            var contract = _subscriptions.FiveSecBars.First(c => c.Value == reqId).Key;
            
            _logger.Debug($"FiveSecondsBarReceived for {contract}");

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
                    continue;

                if(barLength == BarLength._5Sec)
                {
                    Bar5SecReceived?.Invoke(contract, bar);
                    return;
                }

                int sec = (int)barLength;
                if (list.Count > (sec / 5) + 1 && (bar.Time.Second % sec) == 0)
                {
                    var newBar = MakeBar(list, barLength);
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

        public void SubscribeToBars(BarLength barLength, Action<Contract, Bar> callback)
        {
            switch (barLength)
            {
                case BarLength._5Sec: Bar5SecReceived += callback; break;
                case BarLength._1Min: Bar1MinReceived += callback; break;
                default: throw new NotImplementedException();
            }
        }

        public void UnsubscribeToBars(BarLength barLength, Action<Contract, Bar> callback)
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

        public void CancelOrder(Order order)
        {
            _orderManager.CancelOrder(order);
        }

        public void CancelAllOrders() => _orderManager.CancelAllOrders();

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
            _client.RequestPnL(reqId, contract.Id);
        }

        public void CancelPnLSubscription(Contract contract)
        {
            if (_subscriptions.Pnl.ContainsKey(contract))
            {
                _client.CancelPnL(_subscriptions.Pnl[contract]);
                _subscriptions.Pnl.Remove(contract);
            }
        }

        public IEnumerable<Bar> GetPastBars(Contract contract, BarLength barLength, int count)
        {
            return _client.GetHistoricalDataAsync(NextRequestId, contract, barLength, default(DateTime), count).Result;   
        }
   
        internal IEnumerable<Bar> GetPastBars(Contract contract, BarLength barLength, DateTime endDateTime, int count)
        {
            return _client.GetHistoricalDataAsync(NextRequestId, contract, barLength, endDateTime, count).Result;
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

        public IEnumerable<BidAsk> GetPastBidAsks(Contract contract, DateTime time, int count)
        {
            return _client.RequestHistoricalTicks(NextRequestId, contract, time, count).Result;
        }
    }
}
