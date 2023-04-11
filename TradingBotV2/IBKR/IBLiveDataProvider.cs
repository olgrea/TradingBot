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
            if (barLength == BarLength._1Sec)
            {
                // TODO : use "Last" to implement 1 sec bars?
                throw new NotImplementedException("The lowest resolution for live bars is 5 secs in Interactive Broker");
            }

            int reqId = RequestFiveSecondsBarUpdates(ticker);
            if (!_reqIdsToTicker.ContainsKey(reqId))
            {
                _reqIdsToTicker[reqId] = ticker;
                _barSubscriptions[ticker] = new BarSubscription();
            }

            _barSubscriptions[ticker].BarLengthsWanted.Add(barLength);
            if (_barSubscriptions.Count == 1 && _barSubscriptions.First().Value.BarLengthsWanted.Count == 1)
            {
                SubscribeToRealtimeBarCallback();
            }
        }
        
        protected virtual int RequestFiveSecondsBarUpdates(string ticker)
        {
            var contract = _client.ContractsCache.Get(ticker);
            var reqId = _client.RequestFiveSecondsBarUpdates(contract);
            return reqId;
        }

        protected virtual void SubscribeToRealtimeBarCallback()
        {
            _client.Responses.RealtimeBar += OnFiveSecondsBarReceived;
            Debug.Assert(_client.Responses.RealtimeBar.GetInvocationList().Length == 1);
        }

        protected void OnFiveSecondsBarReceived(int reqId, IBApi.FiveSecBar IBApiBar)
        {
            string ticker = _reqIdsToTicker[reqId];

            //_logger.Debug($"FiveSecondsBarReceived for {contract}");

            var bar = (Bar)IBApiBar;

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
                int reqId = CancelFiveSecondsBarsUpdates(ticker);
                _reqIdsToTicker.Remove(reqId);
                _barSubscriptions.Remove(ticker);
            }

            if (_barSubscriptions.Count == 0)
            {
                UnsubscribeFromRealtimeBarCallback();
            }
        }

        protected virtual int CancelFiveSecondsBarsUpdates(string ticker)
        {
            var contract = _client.ContractsCache.Get(ticker);
            var reqId = _client.CancelFiveSecondsBarsUpdates(contract);
            return reqId;
        }

        protected virtual void UnsubscribeFromRealtimeBarCallback()
        {
            _client.Responses.RealtimeBar -= OnFiveSecondsBarReceived;
        }

        public void RequestBidAskUpdates(string ticker)
        {
            int reqId = RequestBidAskData(ticker);
            if (!_reqIdsToTicker.ContainsKey(reqId))
            {
                _reqIdsToTicker[reqId] = ticker;
                SubscribeToBidAskCallback();
            }
        }

        protected virtual int RequestBidAskData(string ticker)
        {
            var contract = _client.ContractsCache.Get(ticker);
            var reqId = _client.RequestTickByTickData(contract, "BidAsk");
            return reqId;
        }

        protected virtual void SubscribeToBidAskCallback()
        {
            _client.Responses.TickByTickBidAsk += TickByTickBidAsk;
        }

        protected void TickByTickBidAsk(int reqId, IBApi.BidAsk bidAsk)
        {
            if(_reqIdsToTicker.ContainsKey(reqId))
            {
                BidAskReceived?.Invoke(_reqIdsToTicker[reqId], (BidAsk)bidAsk);
            }
        }

        public void CancelBidAskUpdates(string ticker)
        {
            int reqId = CancelBidAskData(ticker);
            if (_reqIdsToTicker.ContainsKey(reqId))
            {
                _reqIdsToTicker.Remove(reqId);
                UnsubscribeFromBidAskDataCallback();
            }
        }

        protected virtual int CancelBidAskData(string ticker)
        {
            var contract = _client.ContractsCache.Get(ticker);
            var reqId = _client.CancelTickByTickData(contract, "BidAsk");
            return reqId;
        }

        protected virtual void UnsubscribeFromBidAskDataCallback()
        {
            _client.Responses.TickByTickBidAsk -= TickByTickBidAsk;
        }

        public void RequestLastTradedPriceUpdates(string ticker)
        {
            int reqId = RequestLastData(ticker);
            if (!_reqIdsToTicker.ContainsKey(reqId))
            {
                _reqIdsToTicker[reqId] = ticker;
                SubscribeToLastDataCallback();
            }
        }

        protected virtual int RequestLastData(string ticker)
        {
            var contract = _client.ContractsCache.Get(ticker);
            var reqId = _client.RequestTickByTickData(contract, "Last");
            return reqId;
        }

        protected virtual void SubscribeToLastDataCallback()
        {
            _client.Responses.TickByTickAllLast += TickByTickLast;
        }

        protected void TickByTickLast(int reqId, IBApi.Last last)
        {
            if (_reqIdsToTicker.ContainsKey(reqId))
            {
                LastReceived?.Invoke(_reqIdsToTicker[reqId], (Last)last);
            }
        }

        public void CancelLastTradedPriceUpdates(string ticker)
        {
            int reqId = CancelLastData(ticker);
            if (_reqIdsToTicker.ContainsKey(reqId))
            {
                _reqIdsToTicker.Remove(reqId);
                UnsubscribeFromLastDataCallback();
            }
        }

        protected virtual int CancelLastData(string ticker)
        {
            var contract = _client.ContractsCache.Get(ticker);
            var reqId = _client.CancelTickByTickData(contract, "Last");
            return reqId;
        }

        protected virtual void UnsubscribeFromLastDataCallback()
        {
            _client.Responses.TickByTickAllLast -= TickByTickLast;
        }
    }
}
