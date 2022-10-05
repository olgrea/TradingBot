using System;
using System.Collections.Generic;
using System.Text;
using NLog;
using TradingBot.Broker.Orders;

namespace TradingBot.Strategies
{
    public abstract class State<TStrategy> : IState where TStrategy : Strategy
    {
        protected TStrategy _strategy;
        public State(TStrategy strategy)
        {
            _strategy = strategy;
        }

        internal ILogger Logger => _strategy.Trader.Logger;

        public abstract IState Evaluate();

        internal IState GetState<TState>() where TState : IState => _strategy.GetState<TState>();

        internal void PlaceOrder(Order o)
        {
            _strategy.PlaceOrder(o);
        }

        internal void PlaceOrder(OrderChain c)
        {
            _strategy.PlaceOrder(c);
        }

        internal bool HasBeenRequested(Order order) => _strategy.HasBeenRequested(order);
        internal bool HasBeenOpened(Order order) => _strategy.HasBeenOpened(order);
        internal bool IsCancelled(Order order) => _strategy.IsCancelled(order);
        internal bool IsExecuted(Order order) => _strategy.IsExecuted(order);
    }
}
