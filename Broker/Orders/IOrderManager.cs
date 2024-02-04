using Broker.IBKR.Orders;

namespace Broker.Orders
{
    public interface IOrderManager
    {
        public Task<OrderPlacedResult> PlaceOrderAsync(string ticker, IBOrder order);
        public Task<OrderPlacedResult> PlaceOrderAsync(string ticker, IBOrder order, CancellationToken token);
        public Task<OrderPlacedResult> ModifyOrderAsync(IBOrder order);
        public Task<OrderPlacedResult> ModifyOrderAsync(IBOrder order, CancellationToken token);
        public Task<OrderStatus> CancelOrderAsync(int orderId);
        public Task<OrderStatus> CancelOrderAsync(int orderId, CancellationToken token);
        public Task<IEnumerable<OrderStatus>> CancelAllOrdersAsync();
        public Task<IEnumerable<OrderStatus>> CancelAllOrdersAsync(CancellationToken token);
        public Task<IEnumerable<OrderExecutedResult>> SellAllPositionsAsync();
        public Task<IEnumerable<OrderExecutedResult>> SellAllPositionsAsync(CancellationToken token);
        public Task<OrderExecutedResult> AwaitExecutionAsync(IBOrder order);
        public Task<OrderExecutedResult> AwaitExecutionAsync(IBOrder order, CancellationToken token);

        public event Action<string, IBOrder, OrderStatus> OrderUpdated;
        public event Action<string, OrderExecution> OrderExecuted;
    }
}
