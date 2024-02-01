namespace Broker.IBKR.Orders
{
    public interface IOrderManager
    {
        public Task<OrderPlacedResult> PlaceOrderAsync(string ticker, Order order);
        public Task<OrderPlacedResult> PlaceOrderAsync(string ticker, Order order, CancellationToken token);
        public Task<OrderPlacedResult> ModifyOrderAsync(Order order);
        public Task<OrderPlacedResult> ModifyOrderAsync(Order order, CancellationToken token);
        public Task<OrderStatus> CancelOrderAsync(int orderId);
        public Task<OrderStatus> CancelOrderAsync(int orderId, CancellationToken token);
        public Task<IEnumerable<OrderStatus>> CancelAllOrdersAsync();
        public Task<IEnumerable<OrderStatus>> CancelAllOrdersAsync(CancellationToken token);
        public Task<IEnumerable<OrderExecutedResult>> SellAllPositionsAsync();
        public Task<IEnumerable<OrderExecutedResult>> SellAllPositionsAsync(CancellationToken token);
        public Task<OrderExecutedResult> AwaitExecutionAsync(Order order);
        public Task<OrderExecutedResult> AwaitExecutionAsync(Order order, CancellationToken token);

        public event Action<string, Order, OrderStatus> OrderUpdated;
        public event Action<string, OrderExecution> OrderExecuted;
    }
}
