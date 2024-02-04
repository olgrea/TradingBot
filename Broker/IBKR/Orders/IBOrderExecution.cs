using System.Globalization;
using Broker.Orders;
using Broker.Utils;

namespace Broker.IBKR.Orders
{
    public class IBOrderExecution : IOrderResult
    {
        public IBOrderExecution(string execId, int orderId)
        {
            ExecId = execId;
            OrderId = orderId;
        }

        internal string ExecId { get; set; }
        public int OrderId { get; set; }
        public DateTime Time { get; set; }
        public string? AcctNumber { get; set; }
        public string? Exchange { get; set; }
        public OrderAction Action { get; set; }
        public double Shares { get; set; }
        public double Price { get; set; }
        public double AvgPrice { get; set; }
        public IBCommissionInfo? CommissionInfo { get; set; }

        public override string ToString()
        {
            return $"[{OrderId}] : {Action} {Shares} price={Price:c} avgPrice={AvgPrice:c} exchange={Exchange} time={Time} execId={ExecId}";
        }

        public static explicit operator IBOrderExecution(IBApi.Execution exec)
        {
            var time = exec.Time.Substring(0, exec.Time.Length - exec.Time.LastIndexOf(' '));

            return new IBOrderExecution(exec.ExecId, exec.OrderId)
            {
                Exchange = exec.Exchange,
                Action = exec.Side == "BOT" ? OrderAction.BUY : OrderAction.SELL,
                Shares = Convert.ToDouble(exec.Shares),
                Price = exec.Price,
                AvgPrice = exec.AvgPrice,

                Time = DateTime.SpecifyKind(DateTime.ParseExact(time, MarketDataUtils.TWSTimeFormat, CultureInfo.InvariantCulture), DateTimeKind.Local)
            };
        }
    }
}
