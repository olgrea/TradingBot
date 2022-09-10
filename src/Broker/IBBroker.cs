using System;
using System.Linq;
using System.Collections.Generic;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Utils;
using System.Collections.ObjectModel;

namespace TradingBot.Broker
{
    internal class IBBroker : IBroker
    {
        static HashSet<int> _clientIds = new HashSet<int>();

        const int DefaultPort = 7496;
        const string DefaultIP = "127.0.0.1";
        int _clientId = 1337;

        TWSClient _client;
        ILogger _logger;
        TWSOrderManager _orderManager;

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

            // TODO : need to find a better way to have events in a collection
            BarReceived = new Dictionary<BarLength, EventElement<Contract, Bar>>();
            foreach (BarLength barLength in Enum.GetValues(typeof(BarLength)).OfType<BarLength>())
            {
                BarReceived.Add(barLength, new EventElement<Contract, Bar>());
            }
        }

        public Dictionary<BarLength, EventElement<Contract, MarketData.Bar>> BarReceived { get; }

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

            foreach(BarLength barLength in Enum.GetValues(typeof(BarLength)).OfType<BarLength>())
            {
                InvokeCallbacks(contract, bar, barLength);
            }
        }

        void InvokeCallbacks(Contract contract, MarketData.Bar bar, BarLength barLength)
        {
            if (!BarReceived[barLength].HasSubscribers)
                return;

            if(barLength == BarLength._5Sec)
            {
                BarReceived[BarLength._5Sec]?.Invoke(contract, bar);
                return;
            }

            var list = _fiveSecBars[contract];
            int sec = (int)barLength;

            if (list.Count > (sec / 5) + 1 && (bar.Time.Second % sec) == 0)
            {
                var newBar = MakeBar(list, sec);
                newBar.BarLength = barLength;
                BarReceived[barLength]?.Invoke(contract, newBar);
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

        public void CancelBarsRequest(Contract contract, BarLength barLength)
        {
            if (!BarReceived[barLength].HasSubscribers)
                return;

            BarReceived[barLength].Clear();

            if(BarReceived.All(c => !c.Value.HasSubscribers))
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

        public List<Bar> GetPastBars(Contract contract, BarLength barLength, int count)
        {
            return _client.GetHistoricalDataAsync(contract, barLength, count).Result;   
        }
    }
}
