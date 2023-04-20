using System.Collections.Concurrent;
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

        protected override void RequestFiveSecondsBarUpdates(string ticker)
        {
            _backtester.EnqueueRequest(() =>
            {
                if (!_subscriptions.FiveSecBars.Contains(ticker))
                {
                    _subscriptions.FiveSecBars.Add(ticker);
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
                    bars[i] = _backtester.MarketData.Bars.GetAsync(ticker, current).Result.First();
                    current = current.AddSeconds(1);
                }

                Bar bar = MarketDataUtils.CombineBars(bars, BarLength._5Sec);
                DateTimeOffset dto = new DateTimeOffset(bar.Time.ToUniversalTime());
                RealtimeBar?.Invoke(ticker, (IBApi.FiveSecBar)bar);
            }
        }

        protected override void CancelFiveSecondsBarsUpdates(string ticker)
        {
            _backtester.EnqueueRequest(() =>
            {
                _subscriptions.FiveSecBars.Remove(ticker);
            });
        }

        protected override void SubscribeToRealtimeBarCallback() => RealtimeBar += OnFiveSecondsBarReceived;

        protected override void UnsubscribeFromRealtimeBarCallback() => RealtimeBar -= OnFiveSecondsBarReceived;

        protected override void RequestBidAskData(string ticker)
        {
            _backtester.EnqueueRequest(() =>
            {
                if (!_subscriptions.BidAsk.Contains(ticker))
                {
                    _subscriptions.BidAsk.Add(ticker);
                }
            });
        }

        void OnClockTick_UpdateBidAsk(DateTime newTime)
        {
            foreach (string ticker in _subscriptions.BidAsk)
            {
                foreach(BidAsk ba in _backtester.MarketData.BidAsks.GetAsync(ticker, newTime).Result)
                {
                    BidAsk?.Invoke(ticker, (IBApi.BidAsk)ba);
                }
            }
        }

        protected override void CancelBidAskData(string ticker)
        {
            _backtester.EnqueueRequest(() =>
            {
                _subscriptions.BidAsk.Remove(ticker);
            });
        }

        protected override void SubscribeToBidAskCallback() => BidAsk += TickByTickBidAsk;
        
        protected override void UnsubscribeFromBidAskDataCallback() => BidAsk -= TickByTickBidAsk;

        protected override void RequestLastData(string ticker)
        {
            _backtester.EnqueueRequest(() =>
            {
                if (!_subscriptions.Last.Contains(ticker))
                {
                    _subscriptions.Last.Add(ticker);
                }
            });
        }

        void OnClockTick_UpdateLast(DateTime newTime)
        {
            foreach (string ticker in _subscriptions.Last)
            {
                foreach (Last last in _backtester.MarketData.Lasts.GetAsync(ticker, newTime).Result)
                {
                    Last?.Invoke(ticker, (IBApi.Last)last);
                }
            }
        }

        protected override void CancelLastData(string ticker)
        {
            _backtester.EnqueueRequest(() => 
            {
                _subscriptions.Last.Remove(ticker); 
            });
        }

        protected override void SubscribeToLastDataCallback() => Last += TickByTickLast;
        
        protected override void UnsubscribeFromLastDataCallback() => Last -= TickByTickLast;
    }
}
