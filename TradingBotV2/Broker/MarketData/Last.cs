using System;

namespace TradingBotV2.Broker.MarketData
{
    public class Last : IMarketData
    {
        public DateTime Time { get; set; }
        public double Price { get; set; }
        public int Size { get; set; }
    }
}
