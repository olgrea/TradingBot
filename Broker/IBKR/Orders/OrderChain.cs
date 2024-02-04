namespace Broker.IBKR.Orders
{
    public class OrderChain
    {
        public OrderChain(IBOrder order, params OrderChain[] attachedOrders)
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

        public IBOrder Order { get; }
        public IBOrder? Parent { get; set; }
        public List<OrderChain> AttachedOrders { get; } = new List<OrderChain>();

        public static implicit operator OrderChain(IBOrder o) => new OrderChain(o);

        public override string ToString()
        {
            List<IBOrder> list = Flatten();
            string str = $"Order chain [{list[0]}";
            for (int i = 1; i < list.Count; i++)
                str += $", {list[i]}";
            return str + "]";
        }

        public List<IBOrder> Flatten()
        {
            var list = new List<IBOrder>();
            Flatten(this, list);
            return list;
        }

        void Flatten(OrderChain o, List<IBOrder> list)
        {
            list.Add(o.Order);
            foreach (OrderChain order in o.AttachedOrders)
            {
                Flatten(order, list);
            }
        }
    }
}
