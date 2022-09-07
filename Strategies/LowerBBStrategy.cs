using System.Collections.Generic;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;
using TradingBot.Indicators;

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
                { nameof(RisingState), new RisingState(this, trader)},
                { nameof(SubmitBuyOrderState), new SubmitBuyOrderState(this, trader)},
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
                if (!Trader.Indicators.BollingerBands.IsReady)
                    return this;
                else
                    return Strategy.States[nameof(MonitoringState)];
            }
        }

        class MonitoringState : StateBase
        {
            public MonitoringState(LowerBBStrategy strat, Trader trader) : base(strat, trader) { }

            Bar _bar;

            public override IState Evaluate()
            {
                if (_bar == null)
                    return this;

                if(_bar.Low <= Trader.Indicators.BollingerBands.LowerBB)
                    return Strategy.States[nameof(OversoldState)];
                else
                    return this;
            }

            public override void SubscribeToData()
            {
                Trader.Broker.Bar5SecReceived += OnBarReceived;
                Trader.Broker.RequestBars(Trader.Contract, BarLength._5Sec);
                base.SubscribeToData();
            }

            void OnBarReceived(Contract contract, Bar bar)
            {
                _bar = bar;
            }

            public override void UnsubscribeToData()
            {
                Trader.Broker.Bar5SecReceived -= OnBarReceived;
                Trader.Broker.CancelBarsRequest(Trader.Contract, BarLength._5Sec);
                base.UnsubscribeToData();
            }
        }

        class OversoldState : StateBase
        {
            public OversoldState(LowerBBStrategy strat, Trader trader) : base(strat, trader) { }

            Bar _bar;

            public override IState Evaluate()
            {
                if (_bar == null || _bar.Low <= Trader.Indicators.BollingerBands.LowerBB)
                    return this;
                else
                    return Strategy.States[nameof(RisingState)];
            }

            public override void SubscribeToData()
            {
                Trader.Broker.Bar1MinReceived += OnBarReceived;
                Trader.Broker.RequestBars(Trader.Contract, BarLength._1Min);
                base.SubscribeToData();
            }

            void OnBarReceived(Contract contract, Bar bar)
            {
                _bar = bar;
            }

            public override void UnsubscribeToData()
            {
                Trader.Broker.Bar1MinReceived -= OnBarReceived;
                Trader.Broker.CancelBarsRequest(Trader.Contract, BarLength._1Min);
                base.UnsubscribeToData();
            }
        }

        class RisingState : StateBase
        {
            public RisingState(LowerBBStrategy strat, Trader trader) : base(strat, trader) { }

            public override IState Evaluate()
            {
                //_counter++;

                //if (bar.Low <= _strat.Trader.Indicators.BollingerBands.LowerBB)
                //    _strat.CurrentState = _strat.States[nameof(OversoldState)]; 
                //else if(_counter < 3)
                //    _strat.CurrentState = this;
                //else
                //    _strat.CurrentState = _strat.States[nameof(SubmitBuyOrderState)];

                return null;
            }
        }

        class SubmitBuyOrderState : StateBase
        {
            public SubmitBuyOrderState(LowerBBStrategy strat, Trader trader) : base(strat, trader) { }
            
            public override IState Evaluate()
            {

                //if (/*order is filled*/)
                //_strat.CurrentState = _strat.States[nameof(BoughtState)];
                //else
                //    return _strat.States[nameof(MonitoringState)];
                return null;
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
