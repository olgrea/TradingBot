using System.Collections.Concurrent;
using System.Diagnostics;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.IBKR;
using TradingBotV2.Utils;

namespace TradingBotV2.Backtesting
{
    internal class BacktesterLiveDataProvider : LiveDataProviderBase
    {
        class ServerSideSubscriptions
        {
            public HashSet<string> FiveSecBars { get; set; } = new HashSet<string>();
            public HashSet<string> BidAsk { get; set; } = new HashSet<string>();
            public HashSet<string> Last { get; set; } = new HashSet<string>();
            
            public Action<string, IBApi.FiveSecBar>? RealtimeBarCallback;
            public Action<string, IBApi.BidAsk>? TickByTickBidAskCallback;
            public Action<string, IBApi.Last>? TickByTickLastCallback;
        }
        
        ServerSideSubscriptions _subscriptions = new ServerSideSubscriptions();
        Backtester _backtester;

        public BacktesterLiveDataProvider(Backtester backtester)
        {
            _backtester = backtester;
            _backtester.ClockTick += OnClockTick_UpdateBar;
            _backtester.ClockTick += OnClockTick_UpdateBidAsk;
            _backtester.ClockTick += OnClockTick_UpdateLast;
        }

        public override void Dispose()
        {
            _subscriptions.RealtimeBarCallback -= OnFiveSecondsBarReceived;
            _subscriptions.TickByTickBidAskCallback -= TickByTickBidAsk;
            _subscriptions.TickByTickLastCallback -= TickByTickLast;

            _backtester.ClockTick -= OnClockTick_UpdateBar;
            _backtester.ClockTick -= OnClockTick_UpdateBidAsk;
            _backtester.ClockTick -= OnClockTick_UpdateLast;
        }

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

        void OnClockTick_UpdateBar(DateTime newTime, CancellationToken token)
        {
            if (newTime.Second % 5 != 0)
                return;

            foreach(string ticker in _subscriptions.FiveSecBars)
            {
                var bars = new Bar[5];
                DateTime current = newTime;
                for (int i = 0; i < 5; i++)
                {
                    token.ThrowIfCancellationRequested();

                    bars[i] = _backtester.GetAsync<Bar>(ticker, current, token).Result.First();
                    current = current.AddSeconds(1);
                }

                Bar bar = MarketDataUtils.CombineBars(bars, BarLength._5Sec);
                DateTimeOffset dto = new DateTimeOffset(bar.Time.ToUniversalTime());
                _subscriptions.RealtimeBarCallback?.Invoke(ticker, (IBApi.FiveSecBar)bar);
            }
        }

        protected override void CancelFiveSecondsBarsUpdates(string ticker)
        {
            _backtester.EnqueueRequest(() =>
            {
                _subscriptions.FiveSecBars.Remove(ticker);
            });
        }

        protected override void SubscribeToRealtimeBarCallback()
        {
            Debug.Assert(_subscriptions.RealtimeBarCallback == null);
            _backtester.EnqueueRequest(() =>
            {
                _subscriptions.RealtimeBarCallback += OnFiveSecondsBarReceived;
            });
        }

        protected override void UnsubscribeFromRealtimeBarCallback()
        {
            _backtester.EnqueueRequest(() =>
            {
                _subscriptions.RealtimeBarCallback -= OnFiveSecondsBarReceived;
            });
        } 

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

        void OnClockTick_UpdateBidAsk(DateTime newTime, CancellationToken token)
        {
            foreach (string ticker in _subscriptions.BidAsk)
            {
                token.ThrowIfCancellationRequested();

                IEnumerable<BidAsk> bidAsks = _backtester.GetAsync<BidAsk>(ticker, newTime, token).Result;
                foreach (BidAsk ba in bidAsks)
                {
                    token.ThrowIfCancellationRequested();
                    _subscriptions.TickByTickBidAskCallback?.Invoke(ticker, (IBApi.BidAsk)ba);
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

        protected override void SubscribeToBidAskCallback()
        {
            Debug.Assert(_subscriptions.TickByTickBidAskCallback == null);
            _backtester.EnqueueRequest(() =>
            {
                _subscriptions.TickByTickBidAskCallback += TickByTickBidAsk;
            });
        }
        
        protected override void UnsubscribeFromBidAskDataCallback()
        {
            _backtester.EnqueueRequest(() =>
            {
                _subscriptions.TickByTickBidAskCallback -= TickByTickBidAsk;
            });
        } 

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

        void OnClockTick_UpdateLast(DateTime newTime, CancellationToken token)
        {
            foreach (string ticker in _subscriptions.Last)
            {
                token.ThrowIfCancellationRequested();

                IEnumerable<Last> lasts = _backtester.GetAsync<Last>(ticker, newTime, token).Result;
                foreach (Last last in lasts)
                {
                    token.ThrowIfCancellationRequested();
                    _subscriptions.TickByTickLastCallback?.Invoke(ticker, (IBApi.Last)last);
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

        protected override void SubscribeToLastDataCallback() 
        {
            Debug.Assert(_subscriptions.TickByTickLastCallback == null);
            _backtester.EnqueueRequest(() =>
            {
                _subscriptions.TickByTickLastCallback += TickByTickLast;
            });
        }
        
        protected override void UnsubscribeFromLastDataCallback()
        {
            _backtester.EnqueueRequest(() =>
            {
                _subscriptions.TickByTickLastCallback -= TickByTickLast;
            });
        }
    }
}
