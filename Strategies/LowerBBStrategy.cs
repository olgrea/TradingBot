using System;
using System.Collections.Generic;
using System.Text;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;

namespace TradingBot.Strategies
{
    public class LowerBBStrategy : IStrategy
    {
        IState _currentState = null;

        public LowerBBStrategy(Contract contract)
        {
            Contract = contract;

            States = new Dictionary<string, IState>()
            {
                { nameof(StartState), new StartState(this)},
                { nameof(MonitoringState), new MonitoringState(this)},
                { nameof(OversoldState), new OversoldState(this)},
                { nameof(RisingState), new RisingState(this)},
                { nameof(SubmitBuyOrderState), new SubmitBuyOrderState(this)},
            };
        }

        public Contract Contract { get; private set; }

        public bool Evaluate(Bar bar, BidAsk bidAsk, out Order order)
        {
            _currentState = _currentState.Evaluate(bar, bidAsk, out order);
            return order != null;
        }

        public BollingerBands BB = new BollingerBands();

        public readonly Dictionary<string, IState> States;

        class StartState : IState
        {
            LowerBBStrategy _strat;
            public StartState(LowerBBStrategy strat)
            {
                _strat = strat;
            }

            public IState Evaluate(Bar bar, BidAsk bidAsk, out Order order)
            {
                order = null;

                _strat.BB.Update(bar);
                if (!_strat.BB.IsReady)
                    return this;
                else
                    return _strat.States[nameof(MonitoringState)];
            }
        }

        class MonitoringState : IState
        {
            LowerBBStrategy _strat;
            public MonitoringState(LowerBBStrategy strat)
            {
                _strat = strat;
            }

            public IState Evaluate(Bar bar, BidAsk bidAsk, out Order order)
            {
                order = null;
                _strat.BB.Update(bar);

                if(bar.Low <= _strat.BB.LowerBB)
                    return _strat.States[nameof(OversoldState)];
                else
                    return this;
            }
        }

        class OversoldState : IState
        {
            LowerBBStrategy _strat;
            public OversoldState(LowerBBStrategy strat)
            {
                _strat = strat;
            }

            public IState Evaluate(Bar bar, BidAsk bidAsk, out Order order)
            {
                order = null;
                _strat.BB.Update(bar);

                if (bar.Low <= _strat.BB.LowerBB)
                    return this;
                else
                    return _strat.States[nameof(RisingState)];
            }
        }

        class RisingState : IState
        {
            LowerBBStrategy _strat;
            public RisingState(LowerBBStrategy strat)
            {
                _strat = strat;
            }

            public IState Evaluate(Bar bar, BidAsk bidAsk, out Order order)
            {
                order = null;
                _strat.BB.Update(bar);

                if (bar.Low <= _strat.BB.LowerBB)
                    return _strat.States[nameof(OversoldState)]; 
                else
                    return _strat.States[nameof(SubmitBuyOrderState)];
            }
        }

        class SubmitBuyOrderState : IState
        {
            LowerBBStrategy _strat;
            public SubmitBuyOrderState(LowerBBStrategy strat)
            {
                _strat = strat;
            }

            public IState Evaluate(Bar bar, BidAsk bidAsk, out Order order)
            {
                _strat.BB.Update(bar);
                
                
                order = new Order();

                //if (/*order is filled*/)
                    return _strat.States[nameof(BoughtState)];
                //else
                //    return _strat.States[nameof(MonitoringState)];
            }
        }

        class BoughtState : IState
        {
            LowerBBStrategy _strat;
            public BoughtState(LowerBBStrategy strat)
            {
                _strat = strat;
            }

            public IState Evaluate(Bar bar, BidAsk bidAsk, out Order order)
            {
                _strat.BB.Update(bar);
                order = null;

                //if (/*order is opened*/)
                    return this;
                //else
                //    return _strat.States[nameof(MonitoringState)];
            }
        }
    }
}
