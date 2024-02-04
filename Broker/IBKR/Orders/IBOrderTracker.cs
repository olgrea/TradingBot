namespace Broker.IBKR.Orders
{
    internal class IBOrderTracker
    {
        public IDictionary<int, string> OrderIdsToTicker { get; private set; } = new Dictionary<int, string>();
        public IDictionary<int, IBOrder> OrdersRequested { get; private set; } = new Dictionary<int, IBOrder>();
        public IDictionary<int, IBOrder> OrdersOpened { get; private set; } = new Dictionary<int, IBOrder>(); // contains already executed/cancelled orders
        public IDictionary<int, IBOrder> OpenOrders { get; private set; } = new Dictionary<int, IBOrder>(); // only contains currently open orders
        public IDictionary<int, IBOrderExecution> OrdersExecuted { get; private set; } = new Dictionary<int, IBOrderExecution>();
        public IDictionary<int, IBOrder> OrdersCancelled { get; private set; } = new Dictionary<int, IBOrder>();
        public IDictionary<string, IBOrderExecution> ExecIdsToExecutions { get; private set; } = new Dictionary<string, IBOrderExecution>();

        public bool HasBeenRequested(IBOrder order) => order != null && order.Id > 0 && OrdersRequested.ContainsKey(order.Id);
        public bool HasBeenOpened(IBOrder order) => order != null && order.Id > 0 && OrdersOpened.ContainsKey(order.Id);
        public bool IsCancelled(IBOrder order) => order != null && order.Id > 0 && OrdersCancelled.ContainsKey(order.Id);
        public bool IsExecuted(IBOrder order, out IBOrderExecution? orderExecution)
        {
            orderExecution = null;
            if (order != null && order.Id > 0 && OrdersExecuted.ContainsKey(order.Id))
            {
                orderExecution = OrdersExecuted[order.Id];
                return true;
            }

            return false;
        }

        public void TrackRequest(string ticker, IBOrder order)
        {
            OrderIdsToTicker[order.Id] = ticker;
            OrdersRequested[order.Id] = order;
        }

        public void TrackOpening(IBOrder order)
        {
            OrdersOpened[order.Id] = order;
            OpenOrders[order.Id] = order;
        }

        public void TrackCancellation(IBOrder order)
        {
            OrdersCancelled[order.Id] = order;
            OpenOrders.Remove(order.Id);
        }

        public void TrackExecution(IBOrderExecution execution)
        {
            OrdersExecuted[execution.OrderId] = execution;
            ExecIdsToExecutions[execution.ExecId] = execution;
            OpenOrders.Remove(execution.OrderId);
        }
    }
}
