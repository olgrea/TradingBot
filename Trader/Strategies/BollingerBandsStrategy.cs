using System.Diagnostics;
using Broker.MarketData;
using Broker.Orders;
using Broker.Utils;
using Trader.Indicators;

namespace Trader.Strategies
{
    /// <summary>
    /// Naive strategy for testing purpose
    /// </summary>
    public class BollingerBandsStrategy : StrategyBase
    {
        LinkedList<Last> _lasts;
        BidAsk _latestBidAsk = new();
        Last _latestLast = new();

        public BollingerBandsStrategy(DateTime start, DateTime end, string ticker, Trader trader) : base(start, end, ticker, trader)
        {
            BollingerBands = new BollingerBands(BarLength._5Sec);
            Indicators = new List<IIndicator>() { BollingerBands };
            _lasts = new LinkedList<Last>();
        }

        BollingerBands BollingerBands { get; init; }

        public override IEnumerable<IIndicator> Indicators { get; init; }

        protected override async Task ExecuteStrategy(DateTime time)
        {
            Debug.Assert(_executeStrategyBlock != null);

            if (time < StartTime)
            {
                _trader.Logger?.Debug($"Strategy not started yet. Starts at {StartTime}");
                return;
            }
            else if (time == EndTime.AddMinutes(-1))
            {
                _executeStrategyBlock.Complete();
                _trader.Logger?.Debug($"Strategy Completed.");
                return;
            }

            _trader.Logger?.Info($"Executing strategy at {time}");
            var signals = BollingerBands.Signals.ToList();

            if (signals.Contains(BollingerBandsSignals.CrossedLowerBandDownward))
            {
                _token.ThrowIfCancellationRequested();

                int qty = (int)Math.Floor(_trader.Account.AvailableBuyingPower / _latestBidAsk.Ask);
                if (qty <= 0)
                    return;

                var buyOrder = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = qty };
                await _trader.Broker.OrderManager.PlaceOrderAsync(_ticker, buyOrder);
            }
            else if (signals.Contains(BollingerBandsSignals.CrossedUpperBandUpward))
            {
                _token.ThrowIfCancellationRequested();

                if (!_trader.Account.Positions.TryGetValue(_ticker, out var position))
                    return;

                int qtyToSell = (int)position.PositionAmount;
                if (qtyToSell <= 0)
                    return;

                var sellOrder = new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = qtyToSell };
                await _trader.Broker.OrderManager.PlaceOrderAsync(_ticker, sellOrder);
            }
        }

        protected override void RequestMarketData()
        {
            _trader.Broker.LiveDataProvider.BarReceived += OnBarReceived;
            _trader.Broker.LiveDataProvider.BidAskReceived += OnBidAskReceived;
            _trader.Broker.LiveDataProvider.LastReceived += OnLastReceived;

            _trader.Broker.LiveDataProvider.RequestBarUpdates(_ticker, BollingerBands.BarLength);
            _trader.Broker.LiveDataProvider.RequestBidAskUpdates(_ticker);
            _trader.Broker.LiveDataProvider.RequestLastTradedPriceUpdates(_ticker);
        }

        protected override void CancelMarketData()
        {
            _trader.Broker.LiveDataProvider.BarReceived -= OnBarReceived;
            _trader.Broker.LiveDataProvider.BidAskReceived -= OnBidAskReceived;
            _trader.Broker.LiveDataProvider.LastReceived -= OnLastReceived;

            _trader.Broker.LiveDataProvider.CancelBarUpdates(_ticker, BollingerBands.BarLength);
            _trader.Broker.LiveDataProvider.CancelBidAskUpdates(_ticker);
            _trader.Broker.LiveDataProvider.CancelLastTradedPriceUpdates(_ticker);
        }

        void OnBarReceived(string ticker, Bar bar)
        {
            if (ticker == _ticker && bar.BarLength == BollingerBands.BarLength)
            {
                _trader.Logger?.Debug($"bar received : {bar}");

                lock (_bars)
                {
                    if (!_bars.ContainsKey(bar.BarLength))
                        _bars[bar.BarLength] = new LinkedList<Bar>();
                    _bars[bar.BarLength].AddLast(bar);
                }

                _lasts.Clear();

                if (BollingerBands.IsReady)
                {
                    BollingerBands.Compute(_bars[bar.BarLength]);
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

                _lasts.AddLast(last);
                var partialBar = MarketDataUtils.MakeBarFromLasts(_lasts, BollingerBands.BarLength);
                if (partialBar is not null)
                {
                    BollingerBands.Compute(_bars[BollingerBands.BarLength].Append(partialBar));
                }
            }
        }
    }
}
