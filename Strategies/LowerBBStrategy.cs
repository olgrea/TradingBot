using System.Collections.Generic;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;
using TradingBot.Indicators;

namespace TradingBot.Strategies
{
    public class LowerBBStrategy : StrategyBase
    {
        public LowerBBStrategy(Trader trader) : base(trader)
        {
            States = new Dictionary<string, IState>()
            {
                { nameof(InitState), new InitState(this)},
                { nameof(MonitoringState), new MonitoringState(this)},
                { nameof(OversoldState), new OversoldState(this)},
                { nameof(RisingState), new RisingState(this)},
                { nameof(SubmitBuyOrderState), new SubmitBuyOrderState(this)},
                { nameof(BoughtState), new BoughtState(this)},
            };
        }

        public override void Start()
        {
            if (CurrentState == null)
            {
                CurrentState = States[nameof(InitState)];
            }
        }

        public override void Stop()
        {
            CurrentState = null;
        }

        class InitState : IState
        {
            LowerBBStrategy _strat;
            public InitState(LowerBBStrategy strat)
            {
                _strat = strat;
            }

            public void Evaluate(Bar bar, BidAsk bidAsk)
            {
                if (!_strat.Trader.Indicators.BollingerBands.IsReady)
                    _strat.CurrentState = this;
                else
                    _strat.CurrentState = _strat.States[nameof(MonitoringState)];
            }

            void OnBarReceived(Contract contract, Bar bar)
            {
                Evaluate(bar, null);
            }
        }

        class MonitoringState : IState
        {
            LowerBBStrategy _strat;
            public MonitoringState(LowerBBStrategy strat)
            {
                _strat = strat;
            }

            public void Evaluate(Bar bar, BidAsk bidAsk)
            {
                if(bar.Low <= _strat.Trader.Indicators.BollingerBands.LowerBB)
                    _strat.CurrentState = _strat.States[nameof(OversoldState)];
                else
                    _strat.CurrentState = this;
            }

            void OnBarReceived(Contract contract, Bar bar)
            {
                Evaluate(bar, null);
            }
        }

        class OversoldState : IState
        {
            LowerBBStrategy _strat;
            public OversoldState(LowerBBStrategy strat)
            {
                _strat = strat;
            }

            public void Evaluate(Bar bar, BidAsk bidAsk)
            {
                if (bar.Low <= _strat.Trader.Indicators.BollingerBands.LowerBB)
                    _strat.CurrentState = this;
                else
                    _strat.CurrentState = _strat.States[nameof(RisingState)];
            }

            void OnBarReceived(Contract contract, Bar bar)
            {
                Evaluate(bar, null);
            }
        }

        class RisingState : IState
        {
            int _counter = 0;

            LowerBBStrategy _strat;
            public RisingState(LowerBBStrategy strat)
            {
                _strat = strat;
            }

            public void Evaluate(Bar bar, BidAsk bidAsk)
            {
                _counter++;

                if (bar.Low <= _strat.Trader.Indicators.BollingerBands.LowerBB)
                    _strat.CurrentState = _strat.States[nameof(OversoldState)]; 
                else if(_counter < 3)
                    _strat.CurrentState = this;
                else
                    _strat.CurrentState = _strat.States[nameof(SubmitBuyOrderState)];
            }

            void OnBarReceived(Contract contract, Bar bar)
            {
                Evaluate(bar, null);
            }
        }

        class SubmitBuyOrderState : IState
        {
            LowerBBStrategy _strat;
            public SubmitBuyOrderState(LowerBBStrategy strat)
            {
                _strat = strat;
            }

            public void Evaluate(Bar bar, BidAsk bidAsk)
            {

                //if (/*order is filled*/)
                //_strat.CurrentState = _strat.States[nameof(BoughtState)];
                //else
                //    return _strat.States[nameof(MonitoringState)];
            }

            void OnBidAskReceived(Contract contract, BidAsk bidAsk)
            {
                Evaluate(null, bidAsk);
            }
        }

        class BoughtState : IState
        {
            LowerBBStrategy _strat;
            public BoughtState(LowerBBStrategy strat)
            {
                _strat = strat;
            }

            public void Evaluate(Bar bar, BidAsk bidAsk)
            {
                //if (/*order is opened*/)
                _strat.CurrentState = this;
                //else
                //    return _strat.States[nameof(MonitoringState)];
            }

            void OnBarReceived(Contract contract, Bar bar)
            {
                Evaluate(bar, null);
            }
        }
    }
}
