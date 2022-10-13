using System;
using System.Collections.Generic;
using System.Text;
using NLog;
using TradingBot.Broker.Orders;

namespace TradingBot.Strategies
{
    public abstract class State<TStrategy> : IState where TStrategy : Strategy
    {
        protected bool _isInitialized = false;
        protected TStrategy _strategy;

        public State(TStrategy strategy)
        {
            _strategy = strategy;
        }

        internal ILogger Logger => _strategy.Trader.Logger;

        public abstract IState Evaluate();

        protected virtual void InitializeOrders()
        {
            _isInitialized = true;
        }

        internal IState GetState<TState>() where TState : IState => _strategy.GetState<TState>();

        internal void PlaceOrder(Order o)
        {
            _strategy.PlaceOrder(o);
        }

        internal void ModifyOrder(Order o)
        {
            if(!IsCancelled(o) && !IsExecuted(o, out _))
                _strategy.ModifyOrder(o);
        }

        internal void CancelOrder(Order o)
        {
            if (!IsCancelled(o) && !IsExecuted(o, out _))
                _strategy.CancelOrder(o);
        }

        internal void PlaceOrder(OrderChain c)
        {
            _strategy.PlaceOrder(c);
        }

        internal bool EvaluateOrder(Order toEvaluate, Order toCancel, out OrderExecution orderExecution)
        {
            orderExecution = null;
            if (HasBeenRequested(toEvaluate) && HasBeenOpened(toEvaluate))
            {
                if (IsExecuted(toEvaluate, out orderExecution) || IsCancelled(toEvaluate))
                {
                    if (toCancel != null && HasBeenRequested(toCancel) && HasBeenOpened(toCancel))
                        CancelOrder(toCancel);

                    return true;
                }
            }

            return false;
        }

        internal bool HasBeenRequested(Order order) => _strategy.HasBeenRequested(order);
        internal bool HasBeenOpened(Order order) => _strategy.HasBeenOpened(order);
        internal bool IsCancelled(Order order) => _strategy.IsCancelled(order);
        internal bool IsExecuted(Order order, out OrderExecution orderExecution) => _strategy.IsExecuted(order, out orderExecution);
    }
}
