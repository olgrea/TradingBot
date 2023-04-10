using System.Diagnostics;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.MarketData.Providers;

namespace TradingBotV2.IBKR
{
    internal class IBLiveDataProvider : ILiveDataProvider
    {
        class BarSubscription
        {
            public LinkedList<Bar> FiveSecBars = new LinkedList<Bar>();
            public HashSet<BarLength> BarLengthsWanted = new HashSet<BarLength>();
        }

        IBClient _client;
        Dictionary<int, string> _reqIdsToTicker = new Dictionary<int, string>();
        Dictionary<string, BarSubscription> _barSubscriptions = new Dictionary<string, BarSubscription>();

        public IBLiveDataProvider(IBClient client)
        {
            _client = client;
        }

        public event Action<string, BidAsk> BidAskReceived;
        public event Action<string, Last> LastReceived;
        public event Action<string, Bar> BarReceived;

        public void RequestBarUpdates(string ticker, BarLength barLength)
        {
            if(barLength == BarLength._1Sec)
            {
                // TODO : use "Last" to implement 1 sec bars?
                throw new NotImplementedException("The lowest resolution for live bars is 5 secs in Interactive Broker");
            }

            var contract = _client.ContractsCache.Get(ticker);
            var reqId = _client.RequestFiveSecondsBarUpdates(contract);
            if (!_reqIdsToTicker.ContainsKey(reqId))
            {
                _reqIdsToTicker[reqId] = ticker;
                _barSubscriptions[ticker] = new BarSubscription();
            }
            
            _barSubscriptions[ticker].BarLengthsWanted.Add(barLength);
            if(_barSubscriptions.Count == 1 && _barSubscriptions.First().Value.BarLengthsWanted.Count == 1)
            {
                _client.Responses.RealtimeBar += OnFiveSecondsBarReceived;
                Debug.Assert(_client.Responses.RealtimeBar.GetInvocationList().Length == 1);
            }
        }

        void OnFiveSecondsBarReceived(int reqId, IBApi.FiveSecBar IBApiBar)
        {
            string ticker = _reqIdsToTicker[reqId];

            //_logger.Debug($"FiveSecondsBarReceived for {contract}");

            var bar = IBApiBar.ToTBBar();

            LinkedList<Bar> list = _barSubscriptions[ticker].FiveSecBars;
            list.AddLast(bar);
            // arbitrarily keeping 10 minutes of bars
            if (list.Count > 5*12*10)
                list.RemoveFirst();

            foreach (BarLength barLength in Enum.GetValues(typeof(BarLength)).OfType<BarLength>())
            {
                if (!_barSubscriptions.ContainsKey(ticker) || !_barSubscriptions[ticker].BarLengthsWanted.Contains(barLength))
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

                    BarReceived?.Invoke(ticker, barToUse);
                }
            }
        }

        public void CancelBarUpdates(string ticker, BarLength barLength)
        {
            if (!_barSubscriptions.ContainsKey(ticker))
                return;

            _barSubscriptions[ticker].BarLengthsWanted.Remove(barLength);
            if(_barSubscriptions[ticker].BarLengthsWanted.Count == 0)
            {
                var contract = _client.ContractsCache.Get(ticker);
                var reqId = _client.CancelFiveSecondsBarsUpdates(contract);
                _reqIdsToTicker.Remove(reqId);
                _barSubscriptions.Remove(ticker);
            }

            if(_barSubscriptions.Count == 0)
            {
                _client.Responses.RealtimeBar -= OnFiveSecondsBarReceived;
            }
        }

        public void RequestBidAskUpdates(string ticker)
        {
            var contract = _client.ContractsCache.Get(ticker);
            var reqId = _client.RequestTickByTickData(contract, "BidAsk");
            if (!_reqIdsToTicker.ContainsKey(reqId))
            {
                _reqIdsToTicker[reqId] = ticker;
                _client.Responses.TickByTickBidAsk += TickByTickBidAsk;
            }
        }

        void TickByTickBidAsk(int reqId, IBApi.BidAsk bidAsk)
        {
            if(_reqIdsToTicker.ContainsKey(reqId))
            {
                BidAskReceived?.Invoke(_reqIdsToTicker[reqId], bidAsk.ToTBBidAsk());
            }
        }

        public void CancelBidAskUpdates(string ticker)
        {
            var contract = _client.ContractsCache.Get(ticker);
            var reqId = _client.CancelTickByTickData(contract, "BidAsk");
            if (_reqIdsToTicker.ContainsKey(reqId))
            {
                _reqIdsToTicker.Remove(reqId);
                _client.Responses.TickByTickBidAsk -= TickByTickBidAsk;
            }
        }

        public void RequestLastTradedPriceUpdates(string ticker)
        {
            var contract = _client.ContractsCache.Get(ticker);
            var reqId = _client.RequestTickByTickData(contract, "Last");
            if (!_reqIdsToTicker.ContainsKey(reqId))
            {
                _reqIdsToTicker[reqId] = ticker;
                _client.Responses.TickByTickAllLast += TickByTickLast;
            }
        }

        void TickByTickLast(int reqId, IBApi.Last last)
        {
            if (_reqIdsToTicker.ContainsKey(reqId))
            {
                LastReceived?.Invoke(_reqIdsToTicker[reqId], last.ToTBLast());
            }
        }

        public void CancelLastTradedPriceUpdates(string ticker)
        {
            var contract = _client.ContractsCache.Get(ticker);
            var reqId = _client.CancelTickByTickData(contract, "Last");
            if (_reqIdsToTicker.ContainsKey(reqId))
            {
                _reqIdsToTicker.Remove(reqId);
                _client.Responses.TickByTickAllLast -= TickByTickLast;
            }
        }
    }
}
