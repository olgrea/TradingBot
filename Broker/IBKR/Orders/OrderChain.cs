namespace Broker.IBKR.Orders
{
    public class OrderChain
    {
        public OrderChain(Order order, params OrderChain[] attachedOrders)
        {
            Order = order;

            if (attachedOrders.Length > 0)
            {
                AttachedOrders.AddRange(attachedOrders);
                foreach (OrderChain attachedOrder in AttachedOrders)
                {
                    attachedOrder.Parent = order;
                }
            }
        }

        public Order Order { get; }
        public Order? Parent { get; set; }
        public List<OrderChain> AttachedOrders { get; } = new List<OrderChain>();

        public static implicit operator OrderChain(Order o) => new OrderChain(o);

        public override string ToString()
        {
            List<Order> list = Flatten();
            string str = $"Order chain [{list[0]}";
            for (int i = 1; i < list.Count; i++)
                str += $", {list[i]}";
            return str + "]";
        }

        public List<Order> Flatten()
        {
            var list = new List<Order>();
            Flatten(this, list);
            return list;
        }

        void Flatten(OrderChain o, List<Order> list)
        {
            list.Add(o.Order);
            foreach (OrderChain order in o.AttachedOrders)
            {
                Flatten(order, list);
            }
        }
    }
}
