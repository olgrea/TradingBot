using TradingBot.Broker.Orders;

namespace TradingBot.Broker.Client.Messages
{
    internal class OrderMessage
    {
        public Contract Contract { get; set; }
        public Order Order { get; set; }
        public OrderStatus OrderStatus { get; set; }
        public OrderState OrderState { get; set; }
        public OrderExecution OrderExecution { get; set; }
        public CommissionInfo CommissionInfo { get; set; }
    }
}
