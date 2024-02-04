using Broker.IBKR.Orders;

namespace Broker.Orders
{
    public interface IOrderManager<TOrder> where TOrder : IOrder
    {
        public Task<IOrderResult> PlaceOrderAsync(string ticker, TOrder order);
        public Task<IOrderResult> PlaceOrderAsync(string ticker, TOrder order, CancellationToken token);
        public Task<IOrderResult> ModifyOrderAsync(TOrder order);
        public Task<IOrderResult> ModifyOrderAsync(TOrder order, CancellationToken token);
        public Task<IOrderResult> CancelOrderAsync(int orderId);
        public Task<IOrderResult> CancelOrderAsync(int orderId, CancellationToken token);
        public Task<IEnumerable<IOrderResult>> CancelAllOrdersAsync();
        public Task<IEnumerable<IOrderResult>> CancelAllOrdersAsync(CancellationToken token);
        public Task<IEnumerable<IOrderResult>> SellAllPositionsAsync();
        public Task<IEnumerable<IOrderResult>> SellAllPositionsAsync(CancellationToken token);
        public Task<IOrderResult> AwaitExecutionAsync(TOrder order);
        public Task<IOrderResult> AwaitExecutionAsync(TOrder order, CancellationToken token);

        public event Action<string, TOrder, IOrderResult> OrderUpdated;
        public event Action<string, IOrderResult> OrderExecuted;
    }
}
