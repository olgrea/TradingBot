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

    internal class IBBroker
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
        public bool IsCancelled(Order order) => _orderManager.IsCancelled(order);
        public bool IsExecuted(Order order, out OrderExecution orderExecution) => _orderManager.IsExecuted(order, out orderExecution); 

        public DateTime GetCurrentTime()
        {
            return DateTimeOffset.FromUnixTimeSeconds(_client.GetCurrentTimeAsync().Result).DateTime.ToLocalTime();
        }

        public async Task<int> GetNextValidOrderId()
        {
            return await _client.GetNextValidOrderIdAsync();
        }

        public async Task<ConnectMessage> Connect()
        {
            return await _client.ConnectAsync(DefaultIP, DefaultPort, _clientId);
        }
                
        public async void Disconnect()
        {
            await _client.DisconnectAsync();
            _clientIds.Remove(_clientId);
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

        public void RequestBidAskUpdates(Contract contract)
        {
            if (_subscriptions.BidAsk.ContainsKey(contract))
                return;

            int reqId = NextRequestId;
            _subscriptions.BidAsk[contract] = reqId;

            _client.RequestTickByTickData(reqId, contract, "BidAsk");
        }

        public void CancelBidAskUpdates(Contract contract)
        {
            if (_subscriptions.BidAsk.ContainsKey(contract))
            {
                _client.CancelTickByTickData(_subscriptions.BidAsk[contract]);
                _subscriptions.BidAsk.Remove(contract);
            }
        }

        public void RequestBarsUpdates(Contract contract, BarLength barLength)
        {
            if (_subscriptions.FiveSecBars.ContainsKey(contract))
                return;

            _logger.Debug($"Requesting bar of length {barLength}");
            int reqId = NextRequestId;
            _subscriptions.FiveSecBars[contract] = reqId;

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
            if (!_fiveSecBars.ContainsKey(contract))
            {
                _fiveSecBars.Add(contract, new LinkedList<MarketData.Bar>());
            }

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

        public void CancelBarsUpdates(Contract contract, BarLength barLength)
        {
            if (!HasSubscribers(barLength))
                return;

            var allBarLengths = Enum.GetValues(typeof(BarLength)).OfType<BarLength>();
            if (allBarLengths.All(b => !HasSubscribers(b)))
            {
                var reqId = _subscriptions.FiveSecBars.First(kvp => kvp.Key == contract).Value;
                _client.CancelFiveSecondsBarsUpdates(reqId);
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
            _client.RequestPnLUpdates(reqId, contract.Id);
        }

        public void CancelPnLUpdates(Contract contract)
        {
            if (_subscriptions.Pnl.ContainsKey(contract))
            {
                _client.CancelPnLUpdates(_subscriptions.Pnl[contract]);
                _subscriptions.Pnl.Remove(contract);
            }
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

        public void InitIndicators(Contract contract, IEnumerable<IIndicator> indicators)
        {
            if (!indicators.Any())
                return;

            var longestTime = indicators.Max(i => i.NbPeriods * (int)i.BarLength);
            var pastBars = GetPastBars(contract, BarLength._5Sec, longestTime/(int)BarLength._5Sec).ToList();

            //TODO : remove bars from indicators? I don't know what I was thinking...
            foreach(Bar bar in pastBars)
            {
                UpdateBarsAndInvoke(contract, bar);
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
