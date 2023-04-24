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

        Dictionary<string, BarSubscription> _barSubscriptions = new Dictionary<string, BarSubscription>();
        HashSet<string> _bidAskSubscriptions = new HashSet<string>();
        HashSet<string> _lastsSubscriptions = new HashSet<string>();

        public IBLiveDataProvider(IBClient client)
        {
            _client = client;
        }

        public event Action<string, BidAsk> BidAskReceived;
        public event Action<string, Last> LastReceived;
        public event Action<string, Bar> BarReceived;

        public virtual void Dispose() { }

        public void RequestBarUpdates(string ticker, BarLength barLength)
        {
            if (barLength == BarLength._1Sec)
            {
                // TODO : use "Last" to implement 1 sec bars?
                throw new NotImplementedException("The lowest resolution for live bars is 5 secs in Interactive Broker");
            }

            RequestFiveSecondsBarUpdates(ticker);
            if (!_barSubscriptions.ContainsKey(ticker))
            {
                _barSubscriptions[ticker] = new BarSubscription();
            }

            _barSubscriptions[ticker].BarLengthsWanted.Add(barLength);
            if (_barSubscriptions.Count == 1 && _barSubscriptions.First().Value.BarLengthsWanted.Count == 1)
            {
                SubscribeToRealtimeBarCallback();
            }
        }
        
        protected virtual void RequestFiveSecondsBarUpdates(string ticker)
        {
            _client.RequestFiveSecondsBarUpdates(ticker);
        }

        protected virtual void SubscribeToRealtimeBarCallback()
        {
            _client.Responses.RealtimeBar += OnFiveSecondsBarReceived;
            Debug.Assert(_client.Responses.RealtimeBar.GetInvocationList().Length == 1);
        }

        protected void OnFiveSecondsBarReceived(string ticker, IBApi.FiveSecBar IBApiBar)
        {
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
                CancelFiveSecondsBarsUpdates(ticker);
                _barSubscriptions.Remove(ticker);
            }

            if (_barSubscriptions.Count == 0)
            {
                UnsubscribeFromRealtimeBarCallback();
            }
        }

        protected virtual void CancelFiveSecondsBarsUpdates(string ticker)
        {
            _client.CancelFiveSecondsBarsUpdates(ticker);
        }

        protected virtual void UnsubscribeFromRealtimeBarCallback()
        {
            _client.Responses.RealtimeBar -= OnFiveSecondsBarReceived;
        }

        public void RequestBidAskUpdates(string ticker)
        {
            RequestBidAskData(ticker);
            if (!_bidAskSubscriptions.Contains(ticker))
            {
                _bidAskSubscriptions.Add(ticker);
                SubscribeToBidAskCallback();
            }
        }

        protected virtual void RequestBidAskData(string ticker)
        {
            _client.RequestTickByTickData(ticker, "BidAsk");
        }

        protected virtual void SubscribeToBidAskCallback()
        {
            _client.Responses.TickByTickBidAsk += TickByTickBidAsk;
        }

        protected void TickByTickBidAsk(string ticker, IBApi.BidAsk bidAsk)
        {
            if(_bidAskSubscriptions.Contains(ticker))
            {
                BidAskReceived?.Invoke(ticker, (BidAsk)bidAsk);
            }
        }

        public void CancelBidAskUpdates(string ticker)
        {
            CancelBidAskData(ticker);
            if (_bidAskSubscriptions.Contains(ticker))
            {
                _bidAskSubscriptions.Remove(ticker);
                UnsubscribeFromBidAskDataCallback();
            }
        }

        protected virtual void CancelBidAskData(string ticker)
        {
            _client.CancelTickByTickData(ticker, "BidAsk");
        }

        protected virtual void UnsubscribeFromBidAskDataCallback()
        {
            _client.Responses.TickByTickBidAsk -= TickByTickBidAsk;
        }

        public void RequestLastTradedPriceUpdates(string ticker)
        {
            RequestLastData(ticker);
            if (!_lastsSubscriptions.Contains(ticker))
            {
                _lastsSubscriptions.Add(ticker);
                SubscribeToLastDataCallback();
            }
        }

        protected virtual void RequestLastData(string ticker)
        {
            _client.RequestTickByTickData(ticker, "Last");
        }

        protected virtual void SubscribeToLastDataCallback()
        {
            _client.Responses.TickByTickAllLast += TickByTickLast;
        }

        protected void TickByTickLast(string ticker, IBApi.Last last)
        {
            if (_lastsSubscriptions.Contains(ticker))
            {
                LastReceived?.Invoke(ticker, (Last)last);
            }
        }

        public void CancelLastTradedPriceUpdates(string ticker)
        {
            CancelLastData(ticker);
            if (_lastsSubscriptions.Contains(ticker))
            {
                _lastsSubscriptions.Remove(ticker);
                UnsubscribeFromLastDataCallback();
            }
        }

        protected virtual void CancelLastData(string ticker)
        {
            _client.CancelTickByTickData(ticker, "Last");
        }

        protected virtual void UnsubscribeFromLastDataCallback()
        {
            _client.Responses.TickByTickAllLast -= TickByTickLast;
        }
    }
}
