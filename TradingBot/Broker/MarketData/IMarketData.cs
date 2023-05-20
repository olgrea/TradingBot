using System;

namespace TradingBot.Broker.MarketData
{
    public interface IMarketData
    {
        DateTime Time { get; set; }
    }
}
