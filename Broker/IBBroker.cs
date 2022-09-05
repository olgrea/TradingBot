using System;
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

        public Dictionary<BarLength, Action<Contract, MarketData.Bar>> BarReceived { get; set; } = new Dictionary<BarLength, Action<Contract, Bar>>();

        public Action<Contract, BidAsk> BidAskReceived
        {
            get => _client.BidAskReceived;
            set => _client.BidAskReceived = value;
        }

        public Action<Position> PositionReceived
        {
            get => _client.PositionReceived;
            set => _client.PositionReceived = value;
        }

        public Action<PnL> PnLReceived
        {
            get => _client.PnLReceived;
            set => _client.PnLReceived = value;
        }

        public Action<ClientMessage> ClientMessageReceived
        {
            get => _client.ClientMessageReceived;
            set => _client.ClientMessageReceived = value;
        }

        //TODO : make sure no callbacks are lost...
        public Action<Contract, Order, OrderState> OrderOpened
        {
            get => _client.OrderOpened;
            set => _client.OrderOpened = value;
        }

        public Action<OrderStatus> OrderStatusChanged
        {
            get => _client.OrderStatusChanged;
            set => _client.OrderStatusChanged = value;
        }

        public Action<Contract, OrderExecution> OrderExecuted
        {
            get => _client.OrderExecuted;
            set => _client.OrderExecuted = value;
        }

        public Action<CommissionInfo> CommissionInfoReceived
        {
            get => _client.CommissionInfoReceived;
            set => _client.CommissionInfoReceived = value;
        }

        public void Connect()
        {
            _client.Connect(DefaultIP, DefaultPort, _clientId);
        }

        public void Disconnect()
        {
            _client.Disconnect();
        }

        public Accounts.Account GetAccount()
        {
            return _client.GetAccount();
        }

        public Contract GetContract(string ticker)
        {
            return _client.GetContract(ticker);
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
            if (!BarReceived.ContainsKey(barLength))
                BarReceived[barLength] = null;

            if(BarReceived.Count == 1)
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
            if (!BarReceived.ContainsKey(barLength))
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

        public void CancelAllBarsRequest(Contract contract)
        {
            if(BarReceived.Count == 0)
                return;

            BarReceived.Clear();
            _client.CancelFiveSecondsBarsRequest(contract);
            _fiveSecBars.Remove(contract);
        }

        public void CancelBarsRequest(Contract contract, BarLength barLength)
        {
            if (!BarReceived.ContainsKey(barLength))
                return;

            BarReceived.Remove(barLength);

            if(BarReceived.Count == 0)
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
