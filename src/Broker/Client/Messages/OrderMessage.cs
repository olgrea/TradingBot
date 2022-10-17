using TradingBot.Broker.Orders;

namespace TradingBot.Broker.Client.Messages
{
    internal abstract class OrderMessage
    {
        public Contract Contract { get; set; }
        public Order Order { get; set; }
    }

    internal class OrderPlacedMessage : OrderMessage
    {
        public OrderStatus OrderStatus { get; set; }
        public OrderState OrderState { get; set; }
    }

    internal class OrderExecutedMessage : OrderMessage
    {
        public OrderExecution OrderExecution { get; set; }
        public CommissionInfo CommissionInfo { get; set; }
    }
}
