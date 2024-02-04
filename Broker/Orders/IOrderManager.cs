using Broker.IBKR.Orders;

namespace Broker.Orders
{
    public interface IOrderManager<TOrder> where TOrder : IOrder
    {
        public Task<IOrderResult> PlaceOrderAsync(string ticker, TOrder order);
        public Task<IOrderResult> PlaceOrderAsync(string ticker, TOrder order, CancellationToken token);
        public Task<IOrderResult> ModifyOrderAsync(TOrder order);
        public Task<IOrderResult> ModifyOrderAsync(TOrder order, CancellationToken token);
        public Task<IBOrderStatus> CancelOrderAsync(int orderId);
        public Task<IBOrderStatus> CancelOrderAsync(int orderId, CancellationToken token);
        public Task<IEnumerable<IBOrderStatus>> CancelAllOrdersAsync();
        public Task<IEnumerable<IBOrderStatus>> CancelAllOrdersAsync(CancellationToken token);
        public Task<IEnumerable<IOrderResult>> SellAllPositionsAsync();
        public Task<IEnumerable<IOrderResult>> SellAllPositionsAsync(CancellationToken token);
        public Task<IOrderResult> AwaitExecutionAsync(TOrder order);
        public Task<IOrderResult> AwaitExecutionAsync(TOrder order, CancellationToken token);

        public event Action<string, TOrder, IBOrderStatus> OrderUpdated;
        public event Action<string, IBOrderExecution> OrderExecuted;
    }
}
