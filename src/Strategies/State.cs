using System;
using System.Collections.Generic;
using System.Text;
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

        public abstract IState Evaluate();
    }
}
