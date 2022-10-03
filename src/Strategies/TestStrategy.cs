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
                if (_strategy.BollingerBands_1Min.Bars.Last.Value.Close <= _strategy.BollingerBands_1Min.LowerBB)
                {
                    if(_order == null)
                        _order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 50 };

                    if(!_strategy.HasBeenRequested(_order))
                    {
                        _strategy.PlaceOrder(_order);
                    }
                }
                
                return (_strategy.HasBeenOpened(_order) && _strategy.IsExecuted(_order)) ? _strategy.GetState<BoughtState>() : this;
            }
        }

        class BoughtState : State<TestStrategy>
        {
            Order _order;

            public BoughtState(TestStrategy strategy) : base(strategy) {}

            public override IState Evaluate()
            {
                if (_strategy.BollingerBands_1Min.Bars.Last.Value.Close >= _strategy.BollingerBands_1Min.UpperBB)
                {
                    if (_order == null)
                        _order = new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = 50 };

                    if (!_strategy.HasBeenRequested(_order))
                    {
                        _strategy.PlaceOrder(_order);
                    }
                }
                
                return (_strategy.HasBeenOpened(_order) && _strategy.IsExecuted(_order)) ? _strategy.GetState<MonitoringState>() : this;
            }
        }

        #endregion States
    }
}
