using TradingBotV2.Broker.MarketData;
using TradingBotV2.IBKR;

namespace TradingBotV2.Backtesting
{
    internal class BacktesterLiveDataProvider : IBLiveDataProvider
    {
        internal class Subscriptions
        {
            public HashSet<string> FiveSecBars { get; set; } = new HashSet<string>();
            public HashSet<string> BidAsk { get; set; } = new HashSet<string>();
            public HashSet<string> Last { get; set; } = new HashSet<string>();
        }
        
        Subscriptions _subscriptions = new Subscriptions();
        Backtester _backtester;

        public BacktesterLiveDataProvider(Backtester backtester)
            : base(null)
        {
            _backtester = backtester;
            _backtester.ClockTick += OnClockTick_UpdateBar;
            _backtester.ClockTick += OnClockTick_UpdateBidAsk;
            _backtester.ClockTick += OnClockTick_UpdateLast;
        }

        event Action<string, IBApi.FiveSecBar> RealtimeBar;
        event Action<string, IBApi.BidAsk> BidAsk;
        event Action<string, IBApi.Last> Last;

        internal Dictionary<string, MarketDataCollections> MarketData { get; set; } = new Dictionary<string, MarketDataCollections>();

        internal void Reset()
        {
            _subscriptions = new Subscriptions();
            RealtimeBar = null;
            BidAsk = null;
            Last = null;
            MarketData = new Dictionary<string, MarketDataCollections>();
        }

        protected override void RequestFiveSecondsBarUpdates(string ticker)
        {
            _backtester.RequestsQueue.Enqueue(() => 
            {
                if(!_subscriptions.FiveSecBars.Contains(ticker))
                {
                    _subscriptions.FiveSecBars.Add(ticker);
                }

                if(!MarketData.ContainsKey(ticker))
                {
                    MarketData[ticker] = new MarketDataCollections();
                }

                if (MarketData[ticker].Bars.Count == 0)
                {
                    var timerange = _backtester.TimeRange;
                    var bars = _backtester.HistoricalDataProvider.GetHistoricalOneSecBarsAsync(ticker, timerange.Item1, timerange.Item2).Result;
                    MarketData[ticker].Bars = bars.ToDictionary(b => b.Time);
                }
            });
        }

        void OnClockTick_UpdateBar(DateTime newTime)
        {
            if (newTime.Second % 5 != 0)
                return;

            foreach(string ticker in _subscriptions.FiveSecBars)
            {
                var bars = new Bar[5];
                DateTime current = newTime;
                for (int i = 0; i < 5; i++)
                {
                    bars[i] = MarketData[ticker].Bars[current];
                    current = current.AddSeconds(1);
                }

                Bar bar = MarketDataUtils.CombineBars(bars, BarLength._5Sec);
                DateTimeOffset dto = new DateTimeOffset(bar.Time.ToUniversalTime());
                RealtimeBar?.Invoke(ticker, (IBApi.FiveSecBar)bar);
            }
        }

        protected override void CancelFiveSecondsBarsUpdates(string ticker)
        {
            _backtester.RequestsQueue.Enqueue(() =>
            {
                _subscriptions.FiveSecBars.Remove(ticker);
            });
        }

        protected override void SubscribeToRealtimeBarCallback() => RealtimeBar += OnFiveSecondsBarReceived;

        protected override void UnsubscribeFromRealtimeBarCallback() => RealtimeBar -= OnFiveSecondsBarReceived;

        protected override void RequestBidAskData(string ticker)
        {
            _backtester.RequestsQueue.Enqueue(() =>
            {
                if (!_subscriptions.BidAsk.Contains(ticker))
                {
                    _subscriptions.BidAsk.Add(ticker);
                }

                if (!MarketData.ContainsKey(ticker))
                {
                    MarketData[ticker] = new MarketDataCollections();
                }

                if (MarketData[ticker].BidAsks.Count == 0)
                {
                    var timerange = _backtester.TimeRange;
                    var bidAsks = _backtester.HistoricalDataProvider.GetHistoricalBidAsksAsync(ticker, timerange.Item1, timerange.Item2).Result;
                    MarketData[ticker].BidAsks = bidAsks
                        .GroupBy(ba => ba.Time)
                        .ToDictionary<IGrouping<DateTime, BidAsk>, DateTime, IEnumerable<BidAsk>>(g => g.Key, g => g);
                }
            });
        }

        void OnClockTick_UpdateBidAsk(DateTime newTime)
        {
            foreach (string ticker in _subscriptions.BidAsk)
            {
                foreach(BidAsk ba in MarketData[ticker].BidAsks[newTime])
                {
                    BidAsk?.Invoke(ticker, (IBApi.BidAsk)ba);
                }
            }
        }

        protected override void CancelBidAskData(string ticker)
        {
            _backtester.RequestsQueue.Enqueue(() =>
            {
                _subscriptions.BidAsk.Remove(ticker);
            });
        }

        protected override void SubscribeToBidAskCallback() => BidAsk += TickByTickBidAsk;
        
        protected override void UnsubscribeFromBidAskDataCallback() => BidAsk -= TickByTickBidAsk;

        protected override void RequestLastData(string ticker)
        {
            _backtester.RequestsQueue.Enqueue(() =>
            {
                if (!_subscriptions.Last.Contains(ticker))
                {
                    _subscriptions.Last.Add(ticker);
                }

                if (!MarketData.ContainsKey(ticker))
                {
                    MarketData[ticker] = new MarketDataCollections();
                }

                if (MarketData[ticker].Lasts.Count == 0)
                {
                    var timerange = _backtester.TimeRange;
                    var lasts = _backtester.HistoricalDataProvider.GetHistoricalLastsAsync(ticker, timerange.Item1, timerange.Item2).Result;
                    MarketData[ticker].Lasts = lasts
                        .GroupBy(ba => ba.Time)
                        .ToDictionary<IGrouping<DateTime, Last>, DateTime, IEnumerable<Last>>(g => g.Key, g => g);
                }
            });
        }

        void OnClockTick_UpdateLast(DateTime newTime)
        {
            foreach (string ticker in _subscriptions.Last)
            {
                foreach (Last last in MarketData[ticker].Lasts[newTime])
                {
                    Last?.Invoke(ticker, (IBApi.Last)last);
                }
            }
        }

        protected override void CancelLastData(string ticker)
        {
            _backtester.RequestsQueue.Enqueue(() => 
            {
                _subscriptions.Last.Remove(ticker); 
            });
        }

        protected override void SubscribeToLastDataCallback() => Last += TickByTickLast;
        
        protected override void UnsubscribeFromLastDataCallback() => Last -= TickByTickLast;
    }
}
