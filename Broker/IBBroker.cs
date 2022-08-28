using System;
using System.Collections.Generic;
using System.Threading;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Utils;

namespace TradingBot.Broker
{
    internal class IBBroker : IBroker
    {
        const int DefaultPort = 7496;
        const string DefaultIP = "127.0.0.1";
        int _clientId = 1337;

        TWSClient _client;
        Account _account;
        ILogger _logger;

        Dictionary<Contract, LinkedList<MarketData.Bar>> _fiveSecBars = new Dictionary<Contract, LinkedList<Bar>>();
        Dictionary<Contract, uint> _counters = new Dictionary<Contract, uint>();

        public IBBroker(ILogger logger)
        {
            _logger = logger;
            
            _client = new TWSClient(logger);
            _client.FiveSecBarReceived += OnFiveSecondsBarReceived;
        }

        Dictionary<Contract, Action<Contract, MarketData.Bar>> _fiveSecBarReceived;

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

        public void RequestBars(Contract contract, Action<Contract, Bar> callback)
        {
            if(!_fiveSecBarReceived.ContainsKey(contract))
            {
                _fiveSecBarReceived[contract] = callback;
                _client.RequestFiveSecondsBars(contract);
            }
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

            _fiveSecBarReceived[contract]?.Invoke(contract, bar);
        }

        Bar MakeBar(LinkedList<Bar> list, int seconds)
        {
            Bar bar = new Bar() { High = Decimal.MinValue, Low = Decimal.MaxValue};
            var e = list.GetEnumerator();

            int nbBars = seconds / 5;
            for (int i = 0; i < nbBars; i++, e.MoveNext())
            {
                Bar current = e.Current;
                if(i == 0)
                {
                    bar.Open = current.Open;
                    bar.Time = current.Time;
                }

                bar.High = Math.Max(bar.High, current.High);
                bar.Low = Math.Min(bar.Low, current.Low);
                bar.Volume += current.Volume;
                bar.TradeAmount += current.TradeAmount;

                if (i == nbBars - 1)
                    bar.Close = current.Close;
            }

            return bar;
        }

        public void CancelBarsRequest(Contract contract)
        {
            if(_fiveSecBarReceived.ContainsKey(contract))
            {
                _client.CancelFiveSecondsBarsRequest(contract);
                _fiveSecBarReceived.Remove(contract);
            }
        }
    }
}
