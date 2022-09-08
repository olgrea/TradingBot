using System.Collections.Generic;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;

namespace TradingBot.Strategies
{
    public class LowerBBStrategy : StrategyBase
    {
        public LowerBBStrategy(Trader trader) : base()
        {
            States = new Dictionary<string, IState>()
            {
                { nameof(InitState), new InitState(this, trader)},
                { nameof(MonitoringState), new MonitoringState(this, trader)},
                { nameof(OversoldState), new OversoldState(this, trader)},
                { nameof(BoughtState), new BoughtState(this, trader)},
            };
        }

        public override void Start()
        {
            if (CurrentState == null)
            {
                CurrentState = States[nameof(InitState)];
                base.Start();
            }
        }

        class InitState : StateBase
        {
            public InitState(LowerBBStrategy strat, Trader trader) : base(strat, trader) { }

            public override IState Evaluate()
            {
                if (!Trader.Indicators[BarLength._1Min].BollingerBands.IsReady)
                    return this;
                else
                    return Strategy.States[nameof(MonitoringState)];
            }
        }

        class MonitoringState : StateBase
        {
            public MonitoringState(LowerBBStrategy strat, Trader trader) : base(strat, trader) { }

            public override IState Evaluate()
            {
                if (Trader.Bar5Sec == null)
                    return this;

                if(Trader.Bar5Sec.Close < Trader.Indicators[BarLength._1Min].BollingerBands.LowerBB)
                    return Strategy.States[nameof(OversoldState)];
                else
                    return this;
            }
        }

        class OversoldState : StateBase
        {
            public OversoldState(LowerBBStrategy strat, Trader trader) : base(strat, trader) { }

            public override IState Evaluate()
            {
                //if (_bar == null || _bar.Low <= Trader.Indicators.BB1Min.LowerBB)
                //    return this;
                //else
                //    return Strategy.States[nameof(RisingState)];

                return this;
            }

            void PlaceOrders(Bar bar)
            {
                //var o = new TrailingStopOrder()
            }

            public override void SubscribeToData()
            {
                Trader.Broker.OrderOpened += OnOrderOpened;
                Trader.Broker.OrderExecuted += OnOrderExecuted;
                base.SubscribeToData();
            }

            void OnOrderOpened(Contract c, Order o, OrderState os)
            {

            }

            void OnOrderExecuted(Contract c, OrderExecution oe)
            {

            }

            public override void UnsubscribeToData()
            {
                Trader.Broker.OrderOpened += OnOrderOpened;
                Trader.Broker.OrderExecuted += OnOrderExecuted;
                base.UnsubscribeToData();
            }
        }

        class BoughtState : StateBase
        {
            public BoughtState(LowerBBStrategy strat, Trader trader) : base(strat, trader) { }

            public override IState Evaluate()
            {
                //if (/*order is opened*/)
                return this;
                //else
                //    return _strat.States[nameof(MonitoringState)];
            }
        }
    }
}
