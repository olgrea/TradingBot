using System;
using System.Collections.Generic;
using System.Text;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;

namespace TradingBot.Strategies
{
    public interface IStrategy
    {
        bool Evaluate(Bar bar, BidAsk bidAsk, out Order order);

        Contract Contract { get; }
    }

    public interface IState
    {
        IState Evaluate(Bar bar, BidAsk bidAsk, out Order order);
    }
}
