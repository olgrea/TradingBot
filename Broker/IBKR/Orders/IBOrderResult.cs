namespace Broker.IBKR.Orders
{
    public class OrderPlacedResult
    {
        public string? Ticker { get; set; }
        public IBOrder? Order { get; set; }
        public IBOrderStatus? OrderStatus { get; set; }
        public IBOrderState? OrderState { get; set; }

        // Set on client-side since no order placement time is returned from server.
        public DateTime? Time { get; set; }
    }

    public class OrderExecutedResult
    {
        public string? Ticker { get; set; }
        public IBOrder? Order { get; set; }
        public IBOrderExecution? OrderExecution { get; set; }
        public DateTime Time => OrderExecution?.Time ?? default;
    }
}
