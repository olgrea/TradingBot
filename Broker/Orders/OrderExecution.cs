using System;

namespace TradingBot.Broker.Orders
{
    public class OrderExecution
    {
        public int OrderId { get; set; }
        public DateTime Time { get; set; }
        public string AcctNumber { get; set; }
        public OrderAction Action { get; set; }
        public double Shares { get; set; }
        public double Price { get; set; }
        public double AvgPrice { get; set; }
    }
}
