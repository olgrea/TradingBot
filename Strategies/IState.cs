using System;
using System.Collections.Generic;
using System.Text;
using TradingBot.Broker.MarketData;

namespace TradingBot.Strategies
{
    public interface IState
    {
        Trader Trader { get; }
        IState Evaluate();
        void SubscribeToData();
        void UnsubscribeToData();
    }
}
