using System;

namespace Broker.MarketData
{
    public interface IMarketData
    {
        DateTime Time { get; set; }
    }
}
