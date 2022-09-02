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
            _client.BidAskReceived += OnBidAskReceived;

            _orderManager = new TWSOrderManager(_client, _logger);
        }

        Dictionary<Contract, Dictionary<BarLength, Action<Contract, MarketData.Bar>>> _barReceived = new Dictionary<Contract, Dictionary<BarLength, Action<Contract, MarketData.Bar>>>();
        
        Dictionary<Contract, Action<Contract, MarketData.Bar>> _oneMinuteBarReceived = new Dictionary<Contract, Action<Contract, MarketData.Bar>>();
        Dictionary<Contract, Action<Contract, BidAsk>> _bidAskReceived = new Dictionary<Contract, Action<Contract, BidAsk>>();

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

        public void RequestBidAsk(Contract contract, Action<Contract, BidAsk> callback)
        {
            if (!_bidAskReceived.ContainsKey(contract))
            {
                _client.RequestBidAsk(contract);
            }

            _bidAskReceived[contract] += callback;
        }

        void OnBidAskReceived(Contract contract, BidAsk bidAsk)
        {
            _bidAskReceived[contract]?.Invoke(contract, bidAsk);
        }

        public void CancelBidAskRequest(Contract contract, Action<Contract, BidAsk> callback)
        {
            if (_bidAskReceived.ContainsKey(contract))
            {
                _bidAskReceived[contract] -= callback;
            }

            if (_bidAskReceived[contract] == null)
            {
                _client.CancelBidAskRequest(contract);
                _bidAskReceived.Remove(contract);
            }
        }

        // TODO : test multiple contract subscriptions
        public void RequestBars(Contract contract, BarLength barLength, Action<Contract, MarketData.Bar> callback)
        {
            if (!_barReceived.ContainsKey(contract))
                _barReceived[contract] = new Dictionary<BarLength, Action<Contract, MarketData.Bar>>();

            if (!_barReceived[contract].ContainsKey(barLength))
                _barReceived[contract][barLength] += callback;

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
            if (!_barReceived.ContainsKey(contract))
                return;

            if(barLength == BarLength._5Sec && _barReceived[contract].ContainsKey(barLength))
            {
                _barReceived[contract][BarLength._5Sec]?.Invoke(contract, bar);
                return;
            }

            var list = _fiveSecBars[contract];
            int sec = (int)barLength;

            if (_barReceived[contract].ContainsKey(barLength) && list.Count > (sec / 5) + 1 && (bar.Time.Second % sec) == 0)
            {
                var newBar = MakeBar(list, sec);
                newBar.BarLength = barLength;
                _barReceived[contract][barLength]?.Invoke(contract, newBar);
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
            if(!_barReceived.ContainsKey(contract))
                return;

            _barReceived[contract].Clear();
            _barReceived.Remove(contract);
            _client.CancelFiveSecondsBarsRequest(contract);
            _fiveSecBars.Remove(contract);
        }

        public void CancelBarsRequest(Contract contract, BarLength barLength, Action<Contract, MarketData.Bar> callback)
        {
            if (!_barReceived.ContainsKey(contract))
                return;

            if (_barReceived[contract].ContainsKey(barLength))
            {
                _barReceived[contract][barLength] -= callback;

                if(_barReceived[contract][barLength] == null)
                    _barReceived[contract].Remove(barLength);
            }

            if(_barReceived[contract].Count == 0)
            {
                _barReceived.Remove(contract);
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
    }
}
