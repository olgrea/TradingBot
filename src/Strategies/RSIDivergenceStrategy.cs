using System;
using System.Linq;
using System.Collections.Generic;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Indicators;

namespace TradingBot.Strategies
{
    public class RSIDivergenceStrategy : Strategy
    {
        public RSIDivergenceStrategy(Trader trader) : base(trader)
        {
            AddState<InitState>();
            AddState<MonitoringState>();
            AddState<OversoldState>();
            AddState<BoughtState>();
            
            SetStartState<InitState>();

            AddIndicator(new BollingerBands(BarLength._1Min));
            AddIndicator(new RSIDivergence(BarLength._1Min));
            AddIndicator(new RSIDivergence(BarLength._5Sec));
        }

        internal BollingerBands BollingerBands_1Min => GetIndicator<BollingerBands>(BarLength._1Min);
        internal RSIDivergence RSIDivergence_1Min => GetIndicator<RSIDivergence>(BarLength._1Min);
        internal RSIDivergence RSIDivergence_5Sec => GetIndicator<RSIDivergence>(BarLength._5Sec);

        internal List<Order> Orders { get; set; } = new List<Order>();

        #region States

        class InitState : State<RSIDivergenceStrategy>
        {
            public InitState(RSIDivergenceStrategy strategy) : base(strategy) {} 

            public override IState Evaluate()
            {
                if (_strategy.Indicators.All(i => i.IsReady))
                    return GetState<MonitoringState>();
                else
                    return this;
            }
        }

        class MonitoringState : State<RSIDivergenceStrategy>
        {
            public MonitoringState(RSIDivergenceStrategy strategy) : base(strategy) { }

            public override IState Evaluate()
            {
                // We want to find a bar candle that goes below the lower BB
                if (_strategy.BollingerBands_1Min.Bars.Last.Value.Close < _strategy.BollingerBands_1Min.LowerBB
                    && _strategy.RSIDivergence_1Min.Value < 0)
                {
                    Logger.Info($"Lower band reached. RSIDivergence < 0. Switching to 5 sec resolution.");
                    return GetState<OversoldState>();
                }
                else
                    return this;
            }
        }

        class OversoldState : State<RSIDivergenceStrategy>
        {
            public OversoldState(RSIDivergenceStrategy strategy) : base(strategy) { }

            public override IState Evaluate()
            {
                // At this moment, we switch to a 5 secs resolution.

                if (_strategy.RSIDivergence_5Sec.Value < 0)
                    return this;

                if (!_strategy.Orders.Any())
                {
                    //double funds = _strategy.Trader.GetAvailableFunds();
                    double funds = 5000;
                    _strategy.Orders = BuildOrderChain(funds).Flatten();
                }

                var buyOrder = _strategy.Orders.First();

                if (!_strategy.HasBeenRequested(buyOrder))
                {
                    Logger.Info($"RSIDivergence > 0. Placing market BUY order.");
                    PlaceOrder(buyOrder);
                }

                if (HasBeenOpened(buyOrder) && IsExecuted(buyOrder))
                    return GetState<BoughtState>();
                else if(IsCancelled(buyOrder))
                    return GetState<MonitoringState>();
                else
                    return this;
            }

            OrderChain BuildOrderChain(double funds)
            {
                var latestBar = _strategy.RSIDivergence_5Sec.LatestBar;
                int qty = (int)(funds / latestBar.Close);

                var m = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = qty };

                // TODO : manage stop price better. Should be in relation to the bought price. Would need to uses states instead of chain.
                var trl = new TrailingStopOrder { Action = OrderAction.SELL, TotalQuantity = qty, TrailingPercent = latestBar.Close * 0.003 };

                var qtyLeft = qty - (qty / 2);
                var mit = new MarketIfTouchedOrder() { Action = OrderAction.SELL, TotalQuantity = qty / 2, TouchPrice = _strategy.BollingerBands_1Min.MovingAverage };


                var mit2 = new MarketIfTouchedOrder() { Action = OrderAction.SELL, TotalQuantity = qtyLeft, TouchPrice = _strategy.BollingerBands_1Min.UpperBB };
                var stp = new StopOrder() { Action = OrderAction.SELL, TotalQuantity = qtyLeft, StopPrice = _strategy.BollingerBands_1Min.MovingAverage * 0.997 };

                var innerChain = new OrderChain(mit, new List<OrderChain>() { mit2, stp });
                var chain = new OrderChain(m, new List<OrderChain>() { innerChain, trl });

                return chain;
            }
        }

        class BoughtState : State<RSIDivergenceStrategy>
        {
            public BoughtState(RSIDivergenceStrategy strategy) : base(strategy) { }
            public override IState Evaluate()
            {
                // TODO : adjust opened orders? Don't use order chain and just make more states?
                if(_strategy.Orders.All(o => IsExecuted(o) || IsCancelled(o)))
                {
                    _strategy.Orders.Clear();
                    return GetState<MonitoringState>();
                }
                else
                    return this;
            }
        }

        #endregion States
    }
}
