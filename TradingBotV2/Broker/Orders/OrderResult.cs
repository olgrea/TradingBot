namespace TradingBotV2.Broker.Orders
{
    public abstract class OrderResult
    {
        public string Ticker { get; set; }
        public Order Order { get; set; }
    }

    public class OrderPlacedResult : OrderResult
    {
        public OrderStatus OrderStatus { get; set; }
        public OrderState OrderState { get; set; }
    }

    public class OrderExecutedResult : OrderResult
    {
        public OrderExecution OrderExecution { get; set; }
        public CommissionInfo CommissionInfo { get; set; }
    }
}
