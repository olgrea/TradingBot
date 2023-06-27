namespace Broker.Orders
{
    public class OrderPlacedResult
    {
        public string? Ticker { get; set; }
        public Order? Order { get; set; }
        public OrderStatus? OrderStatus { get; set; }
        public OrderState? OrderState { get; set; }

        // Set on client-side since no order placement time is returned from server.
        public DateTime? Time { get; set; }
    }

    public class OrderExecutedResult
    {
        public string? Ticker { get; set; }
        public Order? Order { get; set; }
        public OrderExecution? OrderExecution { get; set; }
        public DateTime Time => OrderExecution?.Time ?? default;
    }
}
