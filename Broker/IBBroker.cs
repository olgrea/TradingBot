using System;
using System.Linq;
using System.Collections.Generic;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Utils;

namespace TradingBot.Broker
{
    public class IBBroker : IBroker
    {
        static HashSet<int> _clientIds = new HashSet<int>();

        const int DefaultPort = 7496;
        const string DefaultIP = "127.0.0.1";
        int _clientId = 1337;

        TWSClient _client;
        ILogger _logger;
        TWSOrderManager _orderManager;

        Dictionary<BarLength, Action<Contract, MarketData.Bar>> _barReceived = new Dictionary<BarLength, Action<Contract, Bar>>();
        Dictionary<Contract, LinkedList<MarketData.Bar>> _fiveSecBars = new Dictionary<Contract, LinkedList<MarketData.Bar>>();

        public IBBroker(int clientId, ILogger logger)
        {
            if (_clientIds.Contains(clientId))
                throw new ArgumentException($"The client id {clientId} is already assigned.");

            _clientId = clientId;
            _clientIds.Add(clientId);

            _logger = logger;

            _client = new TWSClient(logger);
            _client.FiveSecBarReceived += OnFiveSecondsBarReceived;

            _orderManager = new TWSOrderManager(_client, _logger);
        }

        public event Action<Contract, MarketData.Bar> Bar5SecReceived
        {
            add => AddBarCallback(BarLength._5Sec, value);
            remove => RemoveBarCallback(BarLength._5Sec, value);
        }

        public event Action<Contract, MarketData.Bar> Bar1MinReceived
        {
            add => AddBarCallback(BarLength._1Min, value);
            remove => RemoveBarCallback(BarLength._1Min, value);
        }

        void AddBarCallback(BarLength barLength, Action<Contract, MarketData.Bar> callback)
        {
            if (!_barReceived.ContainsKey(barLength))
                _barReceived.Add(barLength, new Action<Contract, Bar>(callback));
            else
                _barReceived[barLength] += callback;
        }

        void RemoveBarCallback(BarLength barLength, Action<Contract, MarketData.Bar> callback)
        {
            if (_barReceived.ContainsKey(barLength))
                _barReceived[barLength] -= callback;

            if(_barReceived[barLength] == null)
                _barReceived.Remove(barLength); 
        }

        public event Action<Contract, BidAsk> BidAskReceived
        {
            add => _client.BidAskReceived += value;
            remove => _client.BidAskReceived -= value;
        }

        public event Action<Position> PositionReceived
        {
            add => _client.PositionReceived += value;
            remove => _client.PositionReceived -= value;
        }

        public event Action<PnL> PnLReceived
        {
            add => _client.PnLReceived += value;
            remove => _client.PnLReceived -= value;
        }

        public event Action<ClientMessage> ClientMessageReceived
        {
            add => _client.ClientMessageReceived += value;
            remove => _client.ClientMessageReceived -= value;
        }

        public event Action<Contract, Order, OrderState> OrderOpened
        {
            add => _client.OrderOpened += value;
            remove => _client.OrderOpened -= value;
        }

        public event Action<OrderStatus> OrderStatusChanged
        {
            add => _client.OrderStatusChanged += value;
            remove => _client.OrderStatusChanged -= value;
        }

        public event Action<Contract, OrderExecution> OrderExecuted
        {
            add => _client.OrderExecuted += value;
            remove => _client.OrderExecuted -= value;
        }

        public event Action<CommissionInfo> CommissionInfoReceived
        {
            add => _client.CommissionInfoReceived += value;
            remove => _client.CommissionInfoReceived -= value;
        }

        public void Connect()
        {
            _client.ConnectAsync(DefaultIP, DefaultPort, _clientId).Wait();
        }

        public void Disconnect()
        {
            _client.Disconnect();
        }

        public Accounts.Account GetAccount()
        {
            return _client.GetAccountAsync().Result;
        }

        public Contract GetContract(string ticker)
        {
            return _client.GetContractsAsync(ticker).Result?.FirstOrDefault();
        }

        public void RequestBidAsk(Contract contract)
        {
            _client.RequestBidAsk(contract);
        }

        public void CancelBidAskRequest(Contract contract)
        {
            _client.CancelBidAskRequest(contract);
        }

        // TODO : test multiple contract subscriptions
        public void RequestBars(Contract contract, BarLength barLength)
        {
            if(_barReceived.Count == 1)
                _client.RequestFiveSecondsBars(contract);
        }

        void OnFiveSecondsBarReceived(Contract contract, MarketData.Bar bar)
        {
            if(!_fiveSecBars.ContainsKey(contract))
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

            foreach(BarLength barLength in Enum.GetValues(typeof(BarLength)))
            {
                InvokeCallbacks(contract, bar, barLength);
            }
        }

        void InvokeCallbacks(Contract contract, MarketData.Bar bar, BarLength barLength)
        {
            if (!_barReceived.ContainsKey(barLength))
                return;

            if(barLength == BarLength._5Sec)
            {
                _barReceived[BarLength._5Sec]?.Invoke(contract, bar);
                return;
            }

            var list = _fiveSecBars[contract];
            int sec = (int)barLength;

            if (list.Count > (sec / 5) + 1 && (bar.Time.Second % sec) == 0)
            {
                var newBar = MakeBar(list, sec);
                newBar.BarLength = barLength;
                _barReceived[barLength]?.Invoke(contract, newBar);
            }
        }

        MarketData.Bar MakeBar(LinkedList<MarketData.Bar> list, int seconds)
        {
            MarketData.Bar bar = new MarketData.Bar() { High = double.MinValue, Low = double.MaxValue};
            var e = list.GetEnumerator();
            e.MoveNext();

            // The 1st bar shouldn't be included.
            e.MoveNext();

            int nbBars = seconds / 5;
            for (int i = 0; i < nbBars; i++, e.MoveNext())
            {
                MarketData.Bar current = e.Current;
                if(i == 0)
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

        public void CancelAllBarsRequest(Contract contract)
        {
            if(_barReceived.Count == 0)
                return;

            _barReceived.Clear();
            _client.CancelFiveSecondsBarsRequest(contract);
            _fiveSecBars.Remove(contract);
        }

        public void CancelBarsRequest(Contract contract, BarLength barLength)
        {
            if (!_barReceived.ContainsKey(barLength))
                return;

            _barReceived.Remove(barLength);

            if(_barReceived.Count == 0)
            {
                _client.CancelFiveSecondsBarsRequest(contract);
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
        }

        public void CancelPositionsSubscription()
        {
            _client.CancelPositionsSubscription();
        }

        public void RequestPnL(Contract contract)
        {
            _client.RequestPnL(contract);
        }

        public void CancelPnLSubscription(Contract contract)
        {
            _client.CancelPnLRequest(contract);
        }
    }
}
