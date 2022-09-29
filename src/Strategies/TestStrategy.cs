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

        internal OrderChain Order { get; set; }

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
            public MonitoringState(TestStrategy strategy) : base(strategy) { }

            public override IState Evaluate()
            {
                if (_strategy.BollingerBands_1Min.Bars.Last.Value.Close < _strategy.BollingerBands_1Min.LowerBB)
                {
                    // TODO : create async method in trader
                    _strategy.Trader.Broker.PlaceOrder(_strategy.Trader.Contract, new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 50 });
                    return _strategy.GetState<BoughtState>();
                }
                else
                    return this;
            }
        }

        class BoughtState : State<TestStrategy>
        {
            public BoughtState(TestStrategy strategy) : base(strategy) { }
            public override IState Evaluate()
            {
                if (_strategy.BollingerBands_1Min.Bars.Last.Value.Close > _strategy.BollingerBands_1Min.UpperBB)
                {
                    // TODO : create async method in trader
                    _strategy.Trader.Broker.PlaceOrder(_strategy.Trader.Contract, new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = 50 });
                    return _strategy.GetState<MonitoringState>();
                }
                else
                    return this;
            }
        }

        #endregion States
    }
}
