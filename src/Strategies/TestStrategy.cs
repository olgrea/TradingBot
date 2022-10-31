using System.Linq;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Indicators;

namespace TradingBot.Strategies
{
    public class TestStrategy : Strategy
    {
        public TestStrategy(Trader trader) : base(trader)
        {
            AddState<InitState>();
            AddState<MonitoringState>();
            AddState<BoughtState>();

            SetStartState<InitState>();

            AddIndicator(new BollingerBands(BarLength._1Min));
        }

        internal BollingerBands BollingerBands_1Min => GetIndicator<BollingerBands>(BarLength._1Min);

        #region States

        class InitState : State<TestStrategy>
        {
            public InitState(TestStrategy strategy) : base(strategy) { }

            public override IState Evaluate()
            {
                if (_strategy.Indicators.All(i => i.IsReady))
                    return _strategy.GetState<MonitoringState>();
                else
                    return this;
            }
        }

        class MonitoringState : State<TestStrategy>
        {
            Order _order;

            public MonitoringState(TestStrategy strategy) : base(strategy) {}

            public override IState Evaluate()
            {
                if (_strategy.LatestBar.Close <= _strategy.BollingerBands_1Min.LatestResult.LowerBand.Value)
                {
                    if(_order == null)
                        _order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 50 };

                    if(!HasBeenRequested(_order))
                    {
                        Logger.Info($"Lower band reached. Placing market BUY order.");
                        PlaceOrder(_order);
                    }
                }
                
                return (HasBeenOpened(_order) && IsExecuted(_order, out _)) ? GetState<BoughtState>() : this;
            }
        }

        class BoughtState : State<TestStrategy>
        {
            Order _order;

            public BoughtState(TestStrategy strategy) : base(strategy) {}

            public override IState Evaluate()
            {
                if (_strategy.LatestBar.Close >= _strategy.BollingerBands_1Min.LatestResult.UpperBand.Value)
                {
                    if (_order == null)
                        _order = new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = 50 };

                    if (!HasBeenRequested(_order))
                    {
                        Logger.Info($"Higher band reached. Placing market SELL order.");
                        PlaceOrder(_order);
                    }
                }
                
                return (HasBeenOpened(_order) && IsExecuted(_order, out _)) ? GetState<MonitoringState>() : this;
            }
        }

        #endregion States
    }
}
