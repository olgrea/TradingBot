using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.Orders;
using TradingBotV2.Indicators;
using TradingBotV2.Utils;

namespace TradingBotV2.Strategies.TestStrategies
{
    public class BollingerBandsStrategy : IStrategy
    {
        Trader _trader;
        string _ticker;

        LinkedList<Bar> _bars;
        LinkedList<Last> _lasts;
        BidAsk _latestBidAsk = new();
        Last _latestLast = new();

        ActionBlock<DateTime>? _executeStrategyBlock;

        public BollingerBandsStrategy(TimeSpan start, TimeSpan end, string ticker, Trader trader)
        {
            _trader = trader;
            _ticker = ticker;

            StartTime = start;
            EndTime = end;

            BollingerBands = new BollingerBands(BarLength._1Min);
            _bars = new LinkedListWithFixedSize<Bar>(BollingerBands.NbWarmupPeriods * 2);
            _lasts = new LinkedList<Last>();
        }

        BollingerBands BollingerBands { get; init; }
        public TimeSpan StartTime { get; init; }
        public TimeSpan EndTime { get; init; }

        public async Task Start()
        {
            await Initialize();
            Debug.Assert(_executeStrategyBlock != null);
            await _executeStrategyBlock.Completion;

            await _trader.Broker.OrderManager.CancelAllOrdersAsync();
            await _trader.Broker.OrderManager.SellAllPositionsAsync();
        }

        async Task ExecuteStrategy(DateTime time)
        {
            Debug.Assert(_executeStrategyBlock != null);
            if (time.TimeOfDay < StartTime)
            {
                return;
            }
            else if (time.TimeOfDay >= EndTime)
            {
                _executeStrategyBlock.Complete();
                return;
            }

            var signals = BollingerBands.Signals.ToList();

            if (signals.Contains(BollingerBandsSignals.Oversold))
            {
                int qty = (int)Math.Round(_trader.Account.AvailableBuyingPower / _latestBidAsk.Ask);
                if (qty <= 0)
                    return;

                var buyOrder = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = qty };
                await _trader.Broker.OrderManager.PlaceOrderAsync(_ticker, buyOrder);

            }
            else if (signals.Contains(BollingerBandsSignals.Overbought))
            {
                if (!_trader.Account.Positions.TryGetValue(_ticker, out var position))
                    return;

                int qtyToSell = (int)position.PositionAmount;
                if (qtyToSell <= 0)
                    return;

                var sellOrder = new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = qtyToSell };
                await _trader.Broker.OrderManager.PlaceOrderAsync(_ticker, sellOrder);
            }
        }

        public async Task Initialize()
        {
            if(_executeStrategyBlock == null)
            {
                _trader.Broker.LiveDataProvider.BarReceived += OnBarReceived;
                _trader.Broker.LiveDataProvider.BidAskReceived += OnBidAskReceived;
                _trader.Broker.LiveDataProvider.LastReceived += OnLastReceived;
                
                _executeStrategyBlock = new ActionBlock<DateTime>(ExecuteStrategy);
                _ = _executeStrategyBlock.Completion.ContinueWith(t =>
                {
                    _trader.Broker.LiveDataProvider.BarReceived -= OnBarReceived;
                    _trader.Broker.LiveDataProvider.BidAskReceived -= OnBidAskReceived;
                    _trader.Broker.LiveDataProvider.LastReceived -= OnLastReceived;
                });

                _trader.Broker.LiveDataProvider.RequestBarUpdates(_ticker, BollingerBands.BarLength);
                _trader.Broker.LiveDataProvider.RequestBidAskUpdates(_ticker);
                _trader.Broker.LiveDataProvider.RequestLastTradedPriceUpdates(_ticker);
            }

            await InitIndicator();
        }

        async Task InitIndicator()
        {
            if (BollingerBands.IsReady)
                return;

            var st = Stopwatch.StartNew();
            _trader.Logger?.Debug($"Initializing Bollinger Bands indicator.");
            var nbSecs = (int)BollingerBands.BarLength;
            int nbOfOneSecBarsNeeded = BollingerBands.NbWarmupPeriods * nbSecs;
            IEnumerable<IMarketData> oneSecBars = Enumerable.Empty<Bar>();

            DateTime serverTime = await _trader.Broker.GetServerTimeAsync();
            var to = serverTime;
            while (oneSecBars.Count() < nbOfOneSecBarsNeeded)
            {
                var from = to.AddSeconds(-nbOfOneSecBarsNeeded);
                from = from.Floor(TimeSpan.FromSeconds(nbSecs));

                if(from.TimeOfDay < MarketDataUtils.PreMarketStartTime)
                    from = to.ToMarketHours(extendedHours: true).Item1;

                var retrieved = await _trader.Broker.HistoricalDataProvider.GetHistoricalDataAsync<Bar>(_ticker, from, to);
                oneSecBars = retrieved.Concat(oneSecBars);

                to = from;
                if(to.TimeOfDay == MarketDataUtils.PreMarketStartTime)
                {
                    // Fetch the ones from the previous market day is still not enough
                    DateOnly previousMarketDay = MarketDataUtils.FindLastOpenDay(to.AddDays(-1), extendedHours: true);
                    to = previousMarketDay.ToDateTime(TimeOnly.FromTimeSpan(MarketDataUtils.AfterHoursEndTime));
                }
            }

            _trader.Logger?.Debug($"1 sec bars retrieved (count : {oneSecBars.Count()}, time : {st.Elapsed})");

            // build bar collections from 1 sec bars
            var combinedBars = new LinkedList<Bar>();
            var tmp = new LinkedList<Bar>();
            IEnumerable<Bar> ttttt = oneSecBars.OrderBy(b => b.Time).Cast<Bar>();

            Bar? last = null;
            foreach (Bar oneSecBar in ttttt)
            {
                if (last != null && last.Time - oneSecBar.Time > TimeSpan.FromSeconds(1))
                    Debugger.Break();

                tmp.AddLast(oneSecBar);
                if (tmp.Count == nbSecs)
                {
                    Bar newBar = MarketDataUtils.CombineBars(tmp, BollingerBands.BarLength);
                    Debug.Assert(newBar.Time.Second % nbSecs == 0);
                    combinedBars.AddLast(newBar);
                    tmp.Clear();
                }

                last = oneSecBar;
            }

            _trader.Logger?.Debug($" {BollingerBands.BarLength} bars built (count : {combinedBars.Count()}, time : {st.Elapsed}).");

            // Update all indicators.
            lock (_bars)
                _bars = new LinkedList<Bar>(combinedBars.OrderBy(b => b.Time).Concat(_bars));

            Debug.Assert(_bars.All(b => b.Time.Second % nbSecs == 0));
            BollingerBands.Compute(_bars);
            Debug.Assert(BollingerBands.IsReady);
            
            st.Stop();
            _trader.Logger?.Debug($" {BollingerBands.BarLength} bars built (count : {combinedBars.Count()}, time : {st.Elapsed}).");
            _trader.Logger?.Info("Indicators Initialized");
        }

        void OnBarReceived(string ticker, Bar bar)
        {
            if(ticker == _ticker && bar.BarLength == BollingerBands.BarLength)
            {
                lock (_bars)
                    _bars.AddLast(bar);
                
                _lasts.Clear();

                if(BollingerBands.IsReady)
                {
                    BollingerBands.Compute(_bars);
                    Debug.Assert(_executeStrategyBlock != null);
                    _executeStrategyBlock.Post(bar.Time);
                }
            }
        }

        void OnBidAskReceived(string ticker, BidAsk ba)
        {
            if (ticker == _ticker)
            {
                _latestBidAsk = ba;
            }
        }

        void OnLastReceived(string ticker, Last last)
        {
            if (ticker == _ticker)
            {
                _latestLast = last;

                //_lasts.AddLast(last);
                //var partialBar = MarketDataUtils.MakeBarFromLasts(_lasts, BollingerBands.BarLength);
                //if(partialBar is not null)
                //{
                //    BollingerBands.Compute(_bars.Append(partialBar));
                //    Debug.Assert(_executeStrategyBlock != null);
                //    _executeStrategyBlock.Post(last.Time);
                //}
            }
        }
    }
}
