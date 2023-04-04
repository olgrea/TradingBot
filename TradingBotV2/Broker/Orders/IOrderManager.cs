namespace TradingBotV2.Broker.Orders
{
    internal interface IOrderManager
    {
        public Task<OrderResult> PlaceOrderAsync(string ticker, Order order);
        public Task<OrderStatus> CancelOrderAsync(int orderId);
    }
}
