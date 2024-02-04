namespace Broker.IBKR.Orders
{
    public class IBOrderChain
    {
        public IBOrderChain(IBOrder order, params IBOrderChain[] attachedOrders)
        {
            Order = order;

            if (attachedOrders.Length > 0)
            {
                AttachedOrders.AddRange(attachedOrders);
                foreach (IBOrderChain attachedOrder in AttachedOrders)
                {
                    attachedOrder.Parent = order;
                }
            }
        }

        public IBOrder Order { get; }
        public IBOrder? Parent { get; set; }
        public List<IBOrderChain> AttachedOrders { get; } = new List<IBOrderChain>();

        public static implicit operator IBOrderChain(IBOrder o) => new IBOrderChain(o);

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

        void Flatten(IBOrderChain o, List<IBOrder> list)
        {
            list.Add(o.Order);
            foreach (IBOrderChain order in o.AttachedOrders)
            {
                Flatten(order, list);
            }
        }
    }
}
