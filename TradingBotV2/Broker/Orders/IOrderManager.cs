namespace TradingBotV2.Broker.Orders
{
    public interface IOrderManager
    {
        public Task<OrderPlacedResult> PlaceOrderAsync(string ticker, Order order);
        public Task<OrderPlacedResult> ModifyOrderAsync(Order order);
        public Task<OrderStatus> CancelOrderAsync(int orderId);
        public Task<IEnumerable<OrderStatus>> CancelAllOrdersAsync();
        public Task<OrderExecutedResult> AwaitExecution(Order order);

        public event Action<string, Order, OrderStatus> OrderUpdated;
        public event Action<string, OrderExecution> OrderExecuted;
    }
}
