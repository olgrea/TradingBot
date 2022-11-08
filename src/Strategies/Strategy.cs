using System;
using System.Linq;
using System.Collections.Generic;
using TradingBot.Indicators;
using InteractiveBrokers.MarketData;
using InteractiveBrokers.Orders;

namespace TradingBot.Strategies
{
    public abstract class Strategy : IStrategy 
    {
        IState _currentState;
        Dictionary<string, IState> _states = new Dictionary<string, IState>();
        Dictionary<BarLength, List<IIndicator>> _indicators = new Dictionary<BarLength, List<IIndicator>>();

        public Strategy(Trader trader)
        {
            Trader = trader;
        }

        public Trader Trader { get; protected set; }
        public IEnumerable<IIndicator> Indicators => _indicators.SelectMany(i => i.Value);

        public Bar LatestBar { get; private set; }

        protected void AddIndicator(IIndicator indicator)
        {
            if (!_indicators.ContainsKey(indicator.BarLength))
            {
                _indicators.Add(indicator.BarLength, new List<IIndicator>());
            }

            _indicators[indicator.BarLength].Add(indicator);
        }

        protected TIndicator GetIndicator<TIndicator>(BarLength barLength) where TIndicator : IIndicator
        {
            if (!_indicators.ContainsKey(barLength))
                return default(TIndicator);

            return _indicators[barLength].OfType<TIndicator>().FirstOrDefault();
        }
        protected void SetStartState<TState>() where TState : IState
        {
            _currentState = GetState<TState>();
        }

        protected void AddState<TState>() where TState : IState
        {
            var t = typeof(TState);
            _states[t.Name] = (TState)Activator.CreateInstance(t, this);
        }

        internal IState GetState<TState>() where TState : IState
        {
            var t = typeof(TState);
            if (!_states.ContainsKey(t.Name))
                throw new ArgumentException("Invalid state type");

            // reset state on state change
            if(_currentState?.GetType() != t)
                _states[t.Name] = (TState)Activator.CreateInstance(t, this);

            return _states[t.Name];
        }

        public void Evaluate()
        {
            if (_currentState == null)
                throw new InvalidOperationException("No starting state has been set");

            var nextState = _currentState?.Evaluate();
            if (nextState != _currentState)
                Trader.Logger.Info($"Strategy {GetType().Name} state change : {_currentState.GetType().Name} -> {nextState.GetType().Name}");
            _currentState = nextState;
        }

        internal void PlaceOrder(Order o)
        {
            Trader.Broker.PlaceOrder(Trader.Contract, o);
        }

        internal void ModifyOrder(Order o)
        {
            Trader.OrderManager.ModifyOrder(Trader.Contract, o);
        }

        internal void CancelOrder(Order o)
        {
            Trader.Broker.CancelOrder(o);
        }

        internal void PlaceOrder(OrderChain c)
        {
            Trader.OrderManager.PlaceOrder(Trader.Contract, c);
        }

        internal bool HasBeenRequested(Order order) => Trader.OrderManager.HasBeenRequested(order);
        internal bool HasBeenOpened(Order order) => Trader.OrderManager.HasBeenOpened(order);
        internal bool IsCancelled(Order order) => Trader.OrderManager.IsCancelled(order);
        internal bool IsExecuted(Order order, out OrderExecution orderExecution) => Trader.OrderManager.IsExecuted(order, out orderExecution);

        public void ComputeIndicators(IEnumerable<Bar> bars)
        {
            LatestBar = bars.Last();
            foreach (var indicator in _indicators[LatestBar.BarLength])
            {
                indicator.Compute(bars.Cast<BarQuote>());
            }
        }
    }
}
