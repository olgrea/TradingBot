using System;
using System.Linq;
using System.Collections.Generic;
using TradingBot.Broker.MarketData;
using TradingBot.Broker;
using TradingBot.Broker.Orders;
using TradingBot.Indicators;
using System.Threading.Tasks;
using System.Diagnostics;

namespace TradingBot.Strategies
{
    public class TestStrategy : Strategy
    {
        public TestStrategy(Trader trader) : base(trader)
        {
            AddState(new InitState(this));
            AddState(new MonitoringState(this));
            AddState(new BoughtState(this));

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
            bool _orderPlaced = false;
            bool _orderExecuted = false;

            public MonitoringState(TestStrategy strategy) : base(strategy) {}

            public override IState Evaluate()
            {
                if (_strategy.BollingerBands_1Min.Bars.Last.Value.Close < _strategy.BollingerBands_1Min.LowerBB)
                {
                    // TODO : create async method in trader?
                    if(!_orderPlaced)
                    {
                        _order = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 50 };
                        _strategy.PlaceOrder(_order);
                    }
                }
                
                return _orderExecuted ? _strategy.GetState<BoughtState>() : this;
            }

            public override void OrderUpdated(OrderStatus os, OrderExecution oe)
            {
                if (_order.Id == os.Info.OrderId)
                {
                    _orderPlaced = os.Status == Status.PreSubmitted || os.Status == Status.Submitted || os.Status == Status.Filled;
                    _orderExecuted = oe != null && oe.OrderId == os.Info.OrderId;
                }
            }
        }

        class BoughtState : State<TestStrategy>
        {
            Order _order;
            bool _orderPlaced = false;
            bool _orderExecuted = false;

            public BoughtState(TestStrategy strategy) : base(strategy) {}

            public override IState Evaluate()
            {
                if (_strategy.BollingerBands_1Min.Bars.Last.Value.Close > _strategy.BollingerBands_1Min.UpperBB)
                {
                    if (!_orderPlaced)
                    {
                        _order = new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = 50 };
                        _strategy.PlaceOrder(_order);
                    }
                }
                
                return _orderExecuted ? _strategy.GetState<MonitoringState>() : this;
            }

            public override void OrderUpdated(OrderStatus os, OrderExecution oe)
            {
                if (_order.Id == os.Info.OrderId)
                {
                    _orderPlaced = os.Status == Status.PreSubmitted || os.Status == Status.Submitted || os.Status == Status.Filled;
                    _orderExecuted = oe != null && oe.OrderId == os.Info.OrderId;
                }
            }
        }

        #endregion States
    }
}
