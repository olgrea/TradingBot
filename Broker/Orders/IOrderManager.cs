using Broker.IBKR.Orders;

namespace Broker.Orders
{
    public interface IOrderManager<TOrder> where TOrder : IOrder
    {
        public Task<OrderPlacedResult> PlaceOrderAsync(string ticker, TOrder order);
        public Task<OrderPlacedResult> PlaceOrderAsync(string ticker, TOrder order, CancellationToken token);
        public Task<OrderPlacedResult> ModifyOrderAsync(TOrder order);
        public Task<OrderPlacedResult> ModifyOrderAsync(TOrder order, CancellationToken token);
        public Task<OrderStatus> CancelOrderAsync(int orderId);
        public Task<OrderStatus> CancelOrderAsync(int orderId, CancellationToken token);
        public Task<IEnumerable<OrderStatus>> CancelAllOrdersAsync();
        public Task<IEnumerable<OrderStatus>> CancelAllOrdersAsync(CancellationToken token);
        public Task<IEnumerable<OrderExecutedResult>> SellAllPositionsAsync();
        public Task<IEnumerable<OrderExecutedResult>> SellAllPositionsAsync(CancellationToken token);
        public Task<OrderExecutedResult> AwaitExecutionAsync(TOrder order);
        public Task<OrderExecutedResult> AwaitExecutionAsync(TOrder order, CancellationToken token);

        public event Action<string, TOrder, OrderStatus> OrderUpdated;
        public event Action<string, OrderExecution> OrderExecuted;
    }
}
