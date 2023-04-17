using System;
using System.Globalization;

namespace TradingBotV2.Broker.Orders
{
    public class OrderExecution
    {
        internal string ExecId { get; set; }
        public int OrderId { get; set; }
        public DateTime Time { get; set; }
        public string AcctNumber { get; set; }
        public string Exchange { get; set; }
        public OrderAction Action { get; set; }
        public double Shares { get; set; }
        public double Price { get; set; }
        public double AvgPrice { get; set; }
        public CommissionInfo CommissionInfo { get; set; }

        public override string ToString()
        {
            return $"execId={ExecId} {Time} : orderId={OrderId} {Action} shares={Shares} price={Price:c} avgPrice={AvgPrice:c} exchange={Exchange}";
        }

        public static explicit operator OrderExecution(IBApi.Execution exec)
        {
            return new OrderExecution()
            {
                ExecId = exec.ExecId,
                OrderId = exec.OrderId,
                Exchange = exec.Exchange,
                Action = exec.Side == "BOT" ? OrderAction.BUY : OrderAction.SELL,
                Shares = exec.Shares,
                Price = exec.Price,
                AvgPrice = exec.AvgPrice,

                // non-standard date format...
                Time = DateTime.ParseExact(exec.Time, "yyyyMMdd  HH:mm:ss", CultureInfo.InvariantCulture)
            };
        }
    }
}
