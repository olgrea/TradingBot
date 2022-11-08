using System;

namespace IBClient.MarketData
{
    public class Last : IMarketData
    {
        public DateTime Time { get; set; }
        public double Price { get; set; }
        public int Size { get; set; }
    }
}
