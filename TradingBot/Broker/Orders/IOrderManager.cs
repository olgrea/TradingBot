namespace TradingBot.Broker.Orders
{
    public interface IOrderManager
    {
        public Task<OrderPlacedResult> PlaceOrderAsync(string ticker, Order order);
        public Task<OrderPlacedResult> ModifyOrderAsync(Order order);
        public Task<OrderStatus> CancelOrderAsync(int orderId);
        public Task<IEnumerable<OrderStatus>> CancelAllOrdersAsync();
        public Task<IEnumerable<OrderExecutedResult>> SellAllPositionsAsync();
        public Task<OrderExecutedResult> AwaitExecutionAsync(Order order);

        public event Action<string, Order, OrderStatus> OrderUpdated;
        public event Action<string, OrderExecution> OrderExecuted;
    }
}
