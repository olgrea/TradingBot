using System.Collections.Generic;

namespace TradingBot.Broker.Orders
{
    public class OrderChain
    {
        public OrderChain(Order order, List<OrderChain> attachedOrders = null)
        {
            Order = order;
            AttachedOrders = attachedOrders ?? new List<OrderChain>();

            foreach (OrderChain attachedOrder in attachedOrders)
            {
                attachedOrder.Parent = order;
            }
        }   

        public Order Order { get; }
        public Order Parent{ get; set; }
        public List<OrderChain> AttachedOrders { get; }

        public static implicit operator OrderChain(Order o) => new OrderChain(o);
    }
}
