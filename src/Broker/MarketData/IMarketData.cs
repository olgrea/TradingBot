using System;

namespace TradingBot.Broker.MarketData
{
    internal interface IMarketData
    {
        DateTime Time { get; set; }
    }
}
