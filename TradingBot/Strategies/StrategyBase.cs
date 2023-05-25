using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using TradingBot.Broker.MarketData;
using TradingBot.Indicators;
using TradingBot.Utils;

namespace TradingBot.Strategies
{
    public abstract class StrategyBase : IStrategy
    {
        protected Trader _trader;
        protected string _ticker;
        protected ConcurrentDictionary<BarLength, LinkedList<Bar>> _bars = new();
        protected ActionBlock<DateTime>? _executeStrategyBlock;
        protected CancellationToken _token = CancellationToken.None;

        protected StrategyBase(DateTime startTime, DateTime endTime, string ticker, Trader trader)
        {
            StartTime = startTime;
            EndTime = endTime;
            _ticker = ticker;
            _trader = trader;
        }

        public DateTime StartTime { get; init; }

        public DateTime EndTime { get; init; }

        public abstract IEnumerable<IIndicator> Indicators { get; init; }

        public virtual async Task Start(CancellationToken token)
        {
            _token = token;
            await Initialize(token);

            _token.ThrowIfCancellationRequested();
            Debug.Assert(_executeStrategyBlock != null);
            await _executeStrategyBlock.Completion;

            _token.ThrowIfCancellationRequested();
            await _trader.Broker.OrderManager.CancelAllOrdersAsync();
            await _trader.Broker.OrderManager.SellAllPositionsAsync();
        }

        public virtual async Task Initialize(CancellationToken token)
        {
            if (_executeStrategyBlock == null || _executeStrategyBlock.Completion.IsCompleted)
            {
                _executeStrategyBlock?.Completion.Dispose();
                _executeStrategyBlock = new ActionBlock<DateTime>(ExecuteStrategy, new ExecutionDataflowBlockOptions() { CancellationToken = token });
                _ = _executeStrategyBlock.Completion.ContinueWith(t => CancelMarketData());
                RequestMarketData();
            }

            await InitIndicators(token);
        }

        protected abstract Task ExecuteStrategy(DateTime time);

        protected abstract void RequestMarketData();

        protected abstract void CancelMarketData();

        protected async Task InitIndicators(CancellationToken token)
        {
            if (!Indicators.Any())
                throw new ArgumentException("Indicators empty.");

            if (Indicators.All(i => i.IsReady))
                return;

            _trader.Logger?.Debug($"Initializing indicators.");

            var largestNbSecs = (int)Indicators.Max(i => i.BarLength);
            var largestNbWarmupPeriods = Indicators.Max(i => i.NbWarmupPeriods);
            int nbOfOneSecBarsNeeded = largestNbWarmupPeriods * largestNbSecs;

            var oneSecBars = await RetrieveOneSecBars(nbOfOneSecBarsNeeded, largestNbSecs, token);

            // build bar collections from 1 sec bars for each bar length
            foreach (IGrouping<BarLength, IIndicator> group in Indicators.GroupBy(i => i.BarLength))
            {
                token.ThrowIfCancellationRequested();

                BarLength barLength = group.Key;
                lock(_bars)
                {
                    if (!_bars.ContainsKey(barLength))
                        _bars[barLength] = new LinkedList<Bar>();

                    var combinedBars = BuildCombinedBars(oneSecBars, barLength, token);
                    Debug.Assert(combinedBars.All(b => b.Time.Second % largestNbSecs == 0));

                    // It's possible that we already have received some live bars. So append them at the end.
                    _bars[barLength] = new LinkedListWithMaxSize<Bar>(2 * largestNbWarmupPeriods, combinedBars.OrderBy(b => b.Time).Concat(_bars[barLength]));

                    foreach (IIndicator indicator in group)
                    {
                        token.ThrowIfCancellationRequested();

                        indicator.Compute(_bars[barLength]);
                        Debug.Assert(indicator.IsReady);
                    }
                }
            }

            token.ThrowIfCancellationRequested();
            _trader.Logger?.Info("Indicators Initialized");
        }

        LinkedList<Bar> BuildCombinedBars(IEnumerable<Bar> oneSecBars, BarLength barLength, CancellationToken token)
        {
            var combinedBars = new LinkedList<Bar>();
            var tmp = new LinkedList<Bar>();

            Bar? last = null;
            int nbSecs = (int)barLength;
            foreach (Bar oneSecBar in oneSecBars.OrderBy(b => b.Time))
            {
                token.ThrowIfCancellationRequested();

                tmp.AddLast(oneSecBar);
                if (tmp.Count == nbSecs)
                {
                    Bar newBar = MarketDataUtils.CombineBars(tmp, barLength);
                    Debug.Assert(newBar.Time.Second % nbSecs == 0);
                    combinedBars.AddLast(newBar);
                    tmp.Clear();
                }

                last = oneSecBar;
            }

            token.ThrowIfCancellationRequested();
            _trader.Logger?.Debug($" bars of length {barLength} built (count : {combinedBars.Count()}).");
            return combinedBars;
        }

        async Task<IEnumerable<Bar>> RetrieveOneSecBars(int nbOfOneSecBarsNeeded, int largestNbSecs, CancellationToken token)
        {
            IEnumerable<IMarketData> oneSecBars = Enumerable.Empty<Bar>();

            var to = await _trader.Broker.GetServerTimeAsync();
            while (oneSecBars.Count() < nbOfOneSecBarsNeeded)
            {
                token.ThrowIfCancellationRequested();

                var from = to.AddSeconds(-nbOfOneSecBarsNeeded);
                from = from.Floor(TimeSpan.FromSeconds(largestNbSecs));

                if (from.TimeOfDay < MarketDataUtils.PreMarketStartTime)
                    from = to.ToMarketHours(extendedHours: true).Item1;

                var retrieved = await _trader.Broker.HistoricalDataProvider.GetHistoricalDataAsync<Bar>(_ticker, from, to, token);
                oneSecBars = retrieved.Concat(oneSecBars);

                to = from;
                if (to.TimeOfDay == MarketDataUtils.PreMarketStartTime)
                {
                    // Fetch the ones from the previous market day if still not enough
                    DateOnly previousMarketDay = MarketDataUtils.FindLastOpenDay(to.AddDays(-1), extendedHours: true);
                    to = previousMarketDay.ToDateTime(TimeOnly.FromTimeSpan(MarketDataUtils.AfterHoursEndTime));
                }
            }

            token.ThrowIfCancellationRequested();
            _trader.Logger?.Debug($"1 sec bars retrieved (count : {oneSecBars.Count()})");
            return oneSecBars.Cast<Bar>();
        }
    }
}
