namespace TradingBotV2.Broker.Orders
{
    public class OrderPlacedResult
    {
        public string Ticker { get; set; }
        public Order Order { get; set; }
        public OrderStatus OrderStatus { get; set; }
        public OrderState OrderState { get; set; }
    }

    public class OrderExecutedResult
    {
        public string Ticker { get; set; }
        public Order Order { get; set; }
        public OrderExecution OrderExecution { get; set; }
    }
}
