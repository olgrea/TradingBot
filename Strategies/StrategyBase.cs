using System;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Strategies
{
    public abstract class StrategyBase : IStrategy
    {
        IState _currentState;

        public StrategyBase(Trader trader)
        {
            Trader = trader;
        }

        public Trader Trader { get; private set; }
        public Dictionary<string, IState> States { get; protected set; }

        public IState CurrentState
        {
            get => _currentState;
            set
            {
                if (value != _currentState)
                {
                    _currentState = value;
                }
            }
        }

        public abstract void Start();
        public abstract void Stop();
        
    }
}
