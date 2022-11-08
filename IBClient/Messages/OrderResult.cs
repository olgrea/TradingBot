using IBClient.Contracts;
using IBClient.Orders;

namespace IBClient.Messages
{
    internal abstract class OrderResult
    {
        public Contract Contract { get; set; }
        public Order Order { get; set; }
    }

    internal class OrderPlacedResult : OrderResult
    {
        public OrderStatus OrderStatus { get; set; }
        public OrderState OrderState { get; set; }
    }

    internal class OrderExecutedResult : OrderResult
    {
        public OrderExecution OrderExecution { get; set; }
        public CommissionInfo CommissionInfo { get; set; }
    }
}
