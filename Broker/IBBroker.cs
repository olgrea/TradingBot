using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Utils;

namespace TradingBot.Broker
{
    public class IBBroker : IBroker
    {
        const int DefaultPort = 7496;
        const string DefaultIP = "127.0.0.1";
        int _clientId = 1337;

        TWSClient _client;
        ILogger _logger;

        Dictionary<Contract, LinkedList<MarketData.Bar>> _fiveSecBars = new Dictionary<Contract, LinkedList<MarketData.Bar>>();
        Dictionary<Contract, uint> _counters = new Dictionary<Contract, uint>();

        public IBBroker(ILogger logger)
        {
            _logger = logger;
            
            _client = new TWSClient(logger);
            _client.FiveSecBarReceived += OnFiveSecondsBarReceived;
            _client.BidAskReceived += OnBidAskReceived;
        }

        Dictionary<Contract, Dictionary<BarLength, Action<Contract, MarketData.Bar>>> _barReceived = new Dictionary<Contract, Dictionary<BarLength, Action<Contract, Bar>>>();
        
        Dictionary<Contract, Action<Contract, MarketData.Bar>> _oneMinuteBarReceived = new Dictionary<Contract, Action<Contract, Bar>>();
        Dictionary<Contract, Action<Contract, BidAsk>> _bidAskReceived = new Dictionary<Contract, Action<Contract, BidAsk>>();

        public void Connect()
        {
            _client.Connect(DefaultIP, DefaultPort, _clientId);
        }

        public void Disconnect()
        {
            _client.Disconnect();
        }

        public Account GetAccount()
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
                _bidAskReceived[contract] = callback;
                _client.RequestBidAsk(contract);
            }
        }

        void OnBidAskReceived(Contract contract, BidAsk bidAsk)
        {
            _bidAskReceived[contract]?.Invoke(contract, bidAsk);
        }

        public void CancelBidAskRequest(Contract contract)
        {
            if (_bidAskReceived.ContainsKey(contract))
            {
                _client.CancelBidAskRequest(contract);
                _bidAskReceived.Remove(contract);
            }
        }

        public void RequestBars(Contract contract, BarLength barLength, Action<Contract, Bar> callback)
        {
            if (!_barReceived.ContainsKey(contract))
                _barReceived[contract] = new Dictionary<BarLength, Action<Contract, Bar>>();

            if (!_barReceived[contract].ContainsKey(barLength))
                _barReceived[contract][barLength] = callback;

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

            if(_barReceived[contract].ContainsKey(BarLength.FiveSec))
                _barReceived[contract][BarLength.FiveSec]?.Invoke(contract, bar);

            if (_barReceived[contract].ContainsKey(BarLength.ThirtySec) && list.Count > (30 / 5) + 1 && (bar.Time.Second % 30) == 0 )
            {
                var thirtySecBar = MakeBar(list, 30);
                thirtySecBar.BarLength = BarLength.ThirtySec;
                _barReceived[contract][BarLength.ThirtySec]?.Invoke(contract, thirtySecBar);
            }

            if (_barReceived[contract].ContainsKey(BarLength.OneMinute) && list.Count > (60 / 5)+1 && bar.Time.Second == 0)
            {
                var oneMinBar = MakeBar(list, 60);
                oneMinBar.BarLength = BarLength.OneMinute;
                _barReceived[contract][BarLength.OneMinute]?.Invoke(contract, oneMinBar);
            }
        }

        Bar MakeBar(LinkedList<Bar> list, int seconds)
        {
            Bar bar = new Bar() { High = Decimal.MinValue, Low = Decimal.MaxValue};
            var e = list.GetEnumerator();
            e.MoveNext();

            // The 1st bar shouldn't be included.
            e.MoveNext();

            int nbBars = seconds / 5;
            for (int i = 0; i < nbBars; i++, e.MoveNext())
            {
                Bar current = e.Current;
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
        }

        public void CancelBarsRequest(Contract contract, BarLength barLength)
        {
            if (!_barReceived.ContainsKey(contract))
                return;

            if (_barReceived[contract].ContainsKey(barLength))
                _barReceived[contract].Remove(barLength);

            if(_barReceived[contract].Count == 0)
            {
                _barReceived.Remove(contract);
                _client.CancelFiveSecondsBarsRequest(contract);
            }
        }
    }
}
