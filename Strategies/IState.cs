using System;
using System.Collections.Generic;
using System.Text;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;

namespace TradingBot.Strategies
{
    internal interface IState
    {
        IState Evaluate(Bar bar, BidAsk bidAsk);
    }
}
