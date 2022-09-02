using System;

namespace TradingBot.Broker.Orders
{
    public class OrderStatus
    {
        public RequestInfo Info { get; set; }
        public Status Status { get; set; }
        public Decimal Filled { get; set; }
        public Decimal Remaining { get; set; }
        public Decimal AvgFillPrice { get; set; }
        public Decimal LastFillPrice { get; set; }
        public Decimal MktCapPrice { get; set; }
    }
}
