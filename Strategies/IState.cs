using System;
using System.Collections.Generic;
using System.Text;
using TradingBot.Broker.MarketData;

namespace TradingBot.Strategies
{
    public interface IState
    {
        void Evaluate(Bar bar, BidAsk bidAsk);
        void SubscribeToMarketData();
        void UnsubscribeToMarketData();
    }
}
