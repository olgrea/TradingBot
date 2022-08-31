using System;

namespace TradingBot.Broker.Orders
{
    internal class OrderStatus
    {
        public int OrderId { get; set; }
        public int ParentId { get; set; }
        public Decimal Filled { get; set; }
        public Decimal Remaining { get; set; }
        public Decimal AvgFillPrice { get; set; }
        public Decimal LastFillPrice { get; set; }
    }
}
