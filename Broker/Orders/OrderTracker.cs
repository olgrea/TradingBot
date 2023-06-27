namespace Broker.Orders
{
    internal class OrderTracker
    {
        public IDictionary<int, string> OrderIdsToTicker { get; private set; } = new Dictionary<int, string>();
        public IDictionary<int, Order> OrdersRequested { get; private set; } = new Dictionary<int, Order>();
        public IDictionary<int, Order> OrdersOpened { get; private set; } = new Dictionary<int, Order>(); // contains already executed/cancelled orders
        public IDictionary<int, Order> OpenOrders { get; private set; } = new Dictionary<int, Order>(); // only contains currently open orders
        public IDictionary<int, OrderExecution> OrdersExecuted { get; private set; } = new Dictionary<int, OrderExecution>();
        public IDictionary<int, Order> OrdersCancelled { get; private set; } = new Dictionary<int, Order>();
        public IDictionary<string, OrderExecution> ExecIdsToExecutions { get; private set; } = new Dictionary<string, OrderExecution>();

        public bool HasBeenRequested(Order order) => order != null && order.Id > 0 && OrdersRequested.ContainsKey(order.Id);
        public bool HasBeenOpened(Order order) => order != null && order.Id > 0 && OrdersOpened.ContainsKey(order.Id);
        public bool IsCancelled(Order order) => order != null && order.Id > 0 && OrdersCancelled.ContainsKey(order.Id);
        public bool IsExecuted(Order order, out OrderExecution? orderExecution)
        {
            orderExecution = null;
            if (order != null && order.Id > 0 && OrdersExecuted.ContainsKey(order.Id))
            {
                orderExecution = OrdersExecuted[order.Id];
                return true;
            }

            return false;
        }

        public void TrackRequest(string ticker, Order order)
        {
            OrderIdsToTicker[order.Id] = ticker;
            OrdersRequested[order.Id] = order;
        }

        public void TrackOpening(Order order)
        {
            OrdersOpened[order.Id] = order;
            OpenOrders[order.Id] = order;
        }

        public void TrackCancellation(Order order)
        {
            OrdersCancelled[order.Id] = order;
            OpenOrders.Remove(order.Id);
        }

        public void TrackExecution(OrderExecution execution)
        {
            OrdersExecuted[execution.OrderId] = execution;
            ExecIdsToExecutions[execution.ExecId] = execution;
            OpenOrders.Remove(execution.OrderId);
        }
    }
}
