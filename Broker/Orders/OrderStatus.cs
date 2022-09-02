using System;

namespace TradingBot.Broker.Orders
{
    public class OrderStatus
    {
        public RequestInfo Info { get; set; }
        public Status Status { get; set; }
        public double Filled { get; set; }
        public double Remaining { get; set; }
        public double AvgFillPrice { get; set; }
        public double LastFillPrice { get; set; }
        public double MktCapPrice { get; set; }
    }
}
