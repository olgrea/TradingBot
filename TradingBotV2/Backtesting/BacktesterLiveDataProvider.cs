using TradingBotV2.Broker.MarketData;
using TradingBotV2.IBKR;

namespace TradingBotV2.Backtesting
{
    internal class BacktesterLiveDataProvider : IBLiveDataProvider
    {
        internal class TickerToRequestId
        {
            public Dictionary<string, int> FiveSecBars { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, int> Last { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, int> BidAsk { get; set; } = new Dictionary<string, int>();
        }

        int _reqIds = 0;
        TickerToRequestId _tickerToReqIds = new TickerToRequestId();
        Backtester _backtester;

        public BacktesterLiveDataProvider(Backtester backtester)
            : base(null)
        {
            _backtester = backtester;
            _backtester.ClockTick += OnClockTick_UpdateBar;
        }

        event Action<int, IBApi.FiveSecBar> RealtimeBar;
        event Action<int, IBApi.BidAsk> BidAsk;
        event Action<int, IBApi.Last> Last;

        internal Dictionary<string, MarketDataCollections> MarketData { get; set; } = new Dictionary<string, MarketDataCollections>();

        protected override int RequestFiveSecondsBarUpdates(string ticker)
        {
            if(!_tickerToReqIds.FiveSecBars.ContainsKey(ticker))
            {
                _tickerToReqIds.FiveSecBars[ticker] = _reqIds++;
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

            return _tickerToReqIds.FiveSecBars[ticker];
        }

        void OnClockTick_UpdateBar(DateTime newTime)
        {
            if (newTime.Second % 5 != 0)
                return;

            foreach(KeyValuePair<string, int> kvp in _tickerToReqIds.FiveSecBars)
            {
                var list = new List<Bar>(5);
                DateTime current = newTime;
                for (int i = 0; i < 5; i++)
                {
                    list[i] = MarketData[kvp.Key].Bars[current];
                    current = current.AddSeconds(1);
                }

                Bar bar = MarketDataUtils.CombineBars(list, BarLength._5Sec);
                DateTimeOffset dto = new DateTimeOffset(bar.Time.ToUniversalTime());
                RealtimeBar?.Invoke(kvp.Value, bar);
            }
        }

        protected override int CancelFiveSecondsBarsUpdates(string ticker)
        {
            _tickerToReqIds.FiveSecBars.Remove(ticker, out int value);
            return value;
        }

        protected override void SubscribeToRealtimeBarCallback() => RealtimeBar += OnFiveSecondsBarReceived;

        protected override void UnsubscribeFromRealtimeBarCallback() => RealtimeBar -= OnFiveSecondsBarReceived;

        protected override int RequestBidAskData(string ticker)
        {
            if (!_tickerToReqIds.BidAsk.ContainsKey(ticker))
            {
                _tickerToReqIds.BidAsk[ticker] = _reqIds++;
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

            return _tickerToReqIds.BidAsk[ticker];
        }

        protected override int CancelBidAskData(string ticker)
        {
            _tickerToReqIds.BidAsk.Remove(ticker, out int value);
            return value;
        }

        protected override void SubscribeToBidAskCallback() => BidAsk += TickByTickBidAsk;
        
        protected override void UnsubscribeFromBidAskDataCallback() => BidAsk -= TickByTickBidAsk;

        protected override int RequestLastData(string ticker)
        {
            if (!_tickerToReqIds.Last.ContainsKey(ticker))
            {
                _tickerToReqIds.Last[ticker] = _reqIds++;
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

            return _tickerToReqIds.BidAsk[ticker];
        }

        protected override int CancelLastData(string ticker)
        {
            _tickerToReqIds.Last.Remove(ticker, out int value);
            return value;
        }

        protected override void SubscribeToLastDataCallback() => Last += TickByTickLast;
        
        protected override void UnsubscribeFromLastDataCallback() => Last -= TickByTickLast;
    }
}
