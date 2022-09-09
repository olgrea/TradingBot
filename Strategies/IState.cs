using System;
using System.Collections.Generic;
using System.Text;
using TradingBot.Broker.MarketData;

namespace TradingBot.Strategies
{
    internal interface IState
    {
        Trader Trader { get; }
        IState Evaluate();
    }
}
