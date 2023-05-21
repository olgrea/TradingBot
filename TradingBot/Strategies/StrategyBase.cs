using System.Diagnostics;
using TradingBot.Broker.MarketData;
using TradingBot.Indicators;
using TradingBot.Utils;

namespace TradingBot.Strategies
{
    public abstract class StrategyBase : IStrategy
    {
        protected Trader _trader;
        protected string _ticker;
        protected Dictionary<BarLength, LinkedList<Bar>> _bars = new();

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

        public abstract Task Initialize();

        public abstract Task Start();

        // TODO : move that to a base class
        protected async Task InitIndicators()
        {
            if (Indicators.All(i => i.IsReady))
                return;

            _trader.Logger?.Debug($"Initializing indicators.");

            var largestNbSecs = (int)Indicators.Max(i => i.BarLength);
            var largestNbWarmupPeriods = Indicators.Max(i => i.NbWarmupPeriods);
            int nbOfOneSecBarsNeeded = largestNbWarmupPeriods * largestNbSecs;

            var oneSecBars = await RetrieveOneSecBars(nbOfOneSecBarsNeeded, largestNbSecs);

            // build bar collections from 1 sec bars for each bar length
            foreach (IGrouping<BarLength, IIndicator> group in Indicators.GroupBy(i => i.BarLength))
            {
                // lock necessary?
                BarLength barLength = group.Key;
                if (!_bars.ContainsKey(barLength))
                {
                    var combinedBars = BuildCombinedBars(oneSecBars, barLength);
                    Debug.Assert(combinedBars.All(b => b.Time.Second % largestNbSecs == 0));
                    _bars[barLength] = new LinkedList<Bar>(combinedBars.OrderBy(b => b.Time));
                }

                foreach (IIndicator indicator in group)
                {
                    indicator.Compute(_bars[barLength]);
                    Debug.Assert(indicator.IsReady);
                }
            }

            _trader.Logger?.Info("Indicators Initialized");
        }

        LinkedList<Bar> BuildCombinedBars(IEnumerable<Bar> oneSecBars, BarLength barLength)
        {
            var combinedBars = new LinkedList<Bar>();
            var tmp = new LinkedList<Bar>();

            Bar? last = null;
            int nbSecs = (int)barLength;
            foreach (Bar oneSecBar in oneSecBars.OrderBy(b => b.Time))
            {
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

            _trader.Logger?.Debug($" bars of length {barLength} built (count : {combinedBars.Count()}).");
            return combinedBars;
        }

        async Task<IEnumerable<Bar>> RetrieveOneSecBars(int nbOfOneSecBarsNeeded, int largestNbSecs)
        {
            IEnumerable<IMarketData> oneSecBars = Enumerable.Empty<Bar>();

            var to = await _trader.Broker.GetServerTimeAsync();
            while (oneSecBars.Count() < nbOfOneSecBarsNeeded)
            {
                var from = to.AddSeconds(-nbOfOneSecBarsNeeded);
                from = from.Floor(TimeSpan.FromSeconds(largestNbSecs));

                if (from.TimeOfDay < MarketDataUtils.PreMarketStartTime)
                    from = to.ToMarketHours(extendedHours: true).Item1;

                var retrieved = await _trader.Broker.HistoricalDataProvider.GetHistoricalDataAsync<Bar>(_ticker, from, to);
                oneSecBars = retrieved.Concat(oneSecBars);

                to = from;
                if (to.TimeOfDay == MarketDataUtils.PreMarketStartTime)
                {
                    // Fetch the ones from the previous market day is still not enough
                    DateOnly previousMarketDay = MarketDataUtils.FindLastOpenDay(to.AddDays(-1), extendedHours: true);
                    to = previousMarketDay.ToDateTime(TimeOnly.FromTimeSpan(MarketDataUtils.AfterHoursEndTime));
                }
            }

            _trader.Logger?.Debug($"1 sec bars retrieved (count : {oneSecBars.Count()})");
            return oneSecBars.Cast<Bar>();
        }
    }
}
