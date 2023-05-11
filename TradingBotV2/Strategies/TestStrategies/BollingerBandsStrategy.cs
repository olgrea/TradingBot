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
        
        LinkedList<Bar> _bars = new LinkedList<Bar>();
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
            else if (signals.Contains(BollingerBandsSignals.Oversold))
            {
                int qtyToSell = (int)_trader.Account.Positions[_ticker].PositionAmount;
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

                _trader.Broker.LiveDataProvider.RequestBarUpdates(_ticker, BarLength._1Min);
                _trader.Broker.LiveDataProvider.RequestBidAskUpdates(_ticker);
                _trader.Broker.LiveDataProvider.RequestLastTradedPriceUpdates(_ticker);
            }

            await InitIndicator();
        }

        async Task InitIndicator()
        {
            if (BollingerBands.IsReady)
                return;

            int nbOfOneSecBarsNeeded = BollingerBands.NbWarmupPeriods * (int)BollingerBands.BarLength;
            IEnumerable<IMarketData> oneSecBars = Enumerable.Empty<Bar>();

            // Get the ones from today : from opening to now
            DateTime currentTime = await _trader.Broker.GetServerTimeAsync();
            if (currentTime.TimeOfDay > MarketDataUtils.MarketStartTime)
            {
                oneSecBars = await _trader.Broker.HistoricalDataProvider.GetHistoricalDataAsync<Bar>(_ticker, currentTime.ToMarketHours().Item1, currentTime);
            }

            int count = oneSecBars.Count();
            while (count < nbOfOneSecBarsNeeded)
            {
                // Not enough. Getting previous market day to fill the rest.
                DateOnly previousMarketDay = MarketDataUtils.FindLastOpenDay(currentTime.AddDays(-1));
                IEnumerable<IMarketData> previousMarketDayBars = await _trader.Broker.HistoricalDataProvider.GetHistoricalDataAsync<Bar>(_ticker, previousMarketDay);
                oneSecBars = oneSecBars.Concat(previousMarketDayBars);

                currentTime = previousMarketDay.ToDateTime(TimeOnly.FromTimeSpan(MarketDataUtils.MarketStartTime));
                count = oneSecBars.Count();
            }

            // build bar collections from 1 sec bars
            _bars = new LinkedList<Bar>();

            var nbSecs = (int)BollingerBands.BarLength;
            var bars = oneSecBars.Cast<Bar>();
            while (bars.Count() > nbSecs)
            {
                _bars.AddLast(MarketDataUtils.CombineBars(bars.Take(nbSecs), BollingerBands.BarLength));
                bars = bars.Skip(nbSecs);
            }

            // Update all indicators.
            BollingerBands.Compute(_bars);
            Debug.Assert(BollingerBands.IsReady);
        }

        void OnBarReceived(string ticker, Bar bar)
        {
            if(ticker == _ticker && bar.BarLength == BarLength._1Min)
            {
                _bars.AddLast(bar);
                Debug.Assert(_executeStrategyBlock != null);
                _executeStrategyBlock.Post(bar.Time);
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
            }
        }
    }
}
