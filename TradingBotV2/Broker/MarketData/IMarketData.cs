using System;

namespace TradingBotV2.Broker.MarketData
{
    public interface IMarketData
    {
        DateTime Time { get; set; }
    }
}
