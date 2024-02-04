using Broker.IBKR.Orders;

namespace Broker.Orders
{
    public interface IOrderManager<TOrder> where TOrder : IOrder
    {
        public Task<OrderPlacedResult> PlaceOrderAsync(string ticker, TOrder order);
        public Task<OrderPlacedResult> PlaceOrderAsync(string ticker, TOrder order, CancellationToken token);
        public Task<OrderPlacedResult> ModifyOrderAsync(TOrder order);
        public Task<OrderPlacedResult> ModifyOrderAsync(TOrder order, CancellationToken token);
        public Task<IBOrderStatus> CancelOrderAsync(int orderId);
        public Task<IBOrderStatus> CancelOrderAsync(int orderId, CancellationToken token);
        public Task<IEnumerable<IBOrderStatus>> CancelAllOrdersAsync();
        public Task<IEnumerable<IBOrderStatus>> CancelAllOrdersAsync(CancellationToken token);
        public Task<IEnumerable<OrderExecutedResult>> SellAllPositionsAsync();
        public Task<IEnumerable<OrderExecutedResult>> SellAllPositionsAsync(CancellationToken token);
        public Task<OrderExecutedResult> AwaitExecutionAsync(TOrder order);
        public Task<OrderExecutedResult> AwaitExecutionAsync(TOrder order, CancellationToken token);

        public event Action<string, TOrder, IBOrderStatus> OrderUpdated;
        public event Action<string, IBOrderExecution> OrderExecuted;
    }
}
