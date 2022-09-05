using System.Collections.Generic;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;
using TradingBot.Strategies.Indicators;

namespace TradingBot.Strategies
{
    public class LowerBBStrategy : IStrategy
    {
        IState _currentState = null;

        public LowerBBStrategy(Contract contract, Trader trader)
        {
            Contract = contract;
            Trader = trader;

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

        public Contract Contract { get; private set; }
        public Trader Trader { get; private set; }

        void IStrategy.Start()
        {
            if (CurrentState == null)
            {
                CurrentState = States[nameof(InitState)];
                Trader.Broker.RequestBars(Contract, BarLength._5Sec, OnBarReceived);
            }
        }

        void IStrategy.Stop()
        {
            CurrentState = null;
            Trader.Broker.CancelBarsRequest(Contract, BarLength._5Sec, OnBarReceived);
        } 

        void OnBarReceived(Contract contract, Bar bar)
        {
            BB.Update(bar);
        }

        public BollingerBands BB = new BollingerBands();

        public readonly Dictionary<string, IState> States;

        public IState CurrentState
        {
            get => _currentState;
            set
            {
                if(value != _currentState)
                {
                    value?.SubscribeToMarketData();
                    _currentState?.UnsubscribeToMarketData();
                    _currentState = value;
                }
            }
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
                if (!_strat.BB.IsReady)
                    _strat.CurrentState = this;
                else
                    _strat.CurrentState = _strat.States[nameof(MonitoringState)];
            }

            void OnBarReceived(Contract contract, Bar bar)
            {
                Evaluate(bar, null);
            }

            public void SubscribeToMarketData()
            {
                _strat.Trader.Broker.RequestBars(_strat.Contract, BarLength._5Sec, OnBarReceived);
            }

            public void UnsubscribeToMarketData()
            {
                _strat.Trader.Broker.CancelBarsRequest(_strat.Contract, BarLength._5Sec, OnBarReceived);
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
                if(bar.Low <= _strat.BB.LowerBB)
                    _strat.CurrentState = _strat.States[nameof(OversoldState)];
                else
                    _strat.CurrentState = this;
            }

            void OnBarReceived(Contract contract, Bar bar)
            {
                Evaluate(bar, null);
            }

            public void SubscribeToMarketData()
            {
                _strat.Trader.Broker.RequestBars(_strat.Contract, BarLength._1Min, OnBarReceived);
            }

            public void UnsubscribeToMarketData()
            {
                _strat.Trader.Broker.CancelBarsRequest(_strat.Contract, BarLength._1Min, OnBarReceived);
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
                if (bar.Low <= _strat.BB.LowerBB)
                    _strat.CurrentState = this;
                else
                    _strat.CurrentState = _strat.States[nameof(RisingState)];
            }

            void OnBarReceived(Contract contract, Bar bar)
            {
                Evaluate(bar, null);
            }

            public void SubscribeToMarketData()
            {
                _strat.Trader.Broker.RequestBars(_strat.Contract, BarLength._10Sec, OnBarReceived);
            }

            public void UnsubscribeToMarketData()
            {
                _strat.Trader.Broker.CancelBarsRequest(_strat.Contract, BarLength._10Sec, OnBarReceived);
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

                if (bar.Low <= _strat.BB.LowerBB)
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

            public void SubscribeToMarketData()
            {
                _strat.Trader.Broker.RequestBars(_strat.Contract, BarLength._10Sec, OnBarReceived);
                _counter = 0;
            }

            public void UnsubscribeToMarketData()
            {
                _strat.Trader.Broker.CancelBarsRequest(_strat.Contract, BarLength._10Sec, OnBarReceived);
                _counter = 0;
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

            public void SubscribeToMarketData()
            {
                _strat.Trader.Broker.RequestBidAsk(_strat.Contract, OnBidAskReceived);
            }

            public void UnsubscribeToMarketData()
            {
                _strat.Trader.Broker.CancelBidAskRequest(_strat.Contract, OnBidAskReceived);
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

            public void SubscribeToMarketData()
            {
                _strat.Trader.Broker.RequestBars(_strat.Contract, BarLength._5Sec, OnBarReceived);
            }

            public void UnsubscribeToMarketData()
            {
                _strat.Trader.Broker.CancelBarsRequest(_strat.Contract, BarLength._5Sec, OnBarReceived);
            }
        }
    }
}
