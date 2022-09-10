using System;

namespace TradingBot.Broker.Orders
{
    internal class OrderExecution
    {
        public string ExecId { get; set; }
        public int OrderId { get; set; }
        public DateTime Time { get; set; }
        public string AcctNumber { get; set; }
        public string Exchange { get; set; }
        public OrderAction Action { get; set; }
        public double Shares { get; set; }
        public double Price { get; set; }
        public double AvgPrice { get; set; }

        public override string ToString()
        {
            return $"execId={ExecId} {Time} : orderId={OrderId} {Action} shares={Shares} price={Price} avgPrice={AvgPrice} exchange={Exchange}";
        }
    }
}
