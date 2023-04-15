namespace TradingBotV2.Broker.Orders
{
    public interface IOrderManager
    {
        public Task<OrderResult> PlaceOrderAsync(string ticker, Order order);
        public Task<OrderResult> ModifyOrderAsync(Order order);
        public Task<OrderStatus> CancelOrderAsync(int orderId);
    }
}
