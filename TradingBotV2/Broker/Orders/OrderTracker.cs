namespace TradingBotV2.Broker.Orders
{
    internal class OrderTracker
    {
        public IDictionary<int, string> OrderIdsToTicker { get; set; } = new Dictionary<int, string>();
        public IDictionary<int, Order> OrdersRequested { get; set; } = new Dictionary<int, Order>();
        public IDictionary<int, Order> OrdersOpened { get; set; } = new Dictionary<int, Order>();
        public IDictionary<int, OrderExecution> OrdersExecuted { get; set; } = new Dictionary<int, OrderExecution>();
        public IDictionary<int, Order> OrdersCancelled { get; set; } = new Dictionary<int, Order>();
        public IDictionary<string, OrderExecution> Executions { get; set; } = new Dictionary<string, OrderExecution>();

        public bool HasBeenRequested(Order order) => order != null && order.Id > 0 && OrdersRequested.ContainsKey(order.Id);
        public bool HasBeenOpened(Order order) => order != null && order.Id > 0 && OrdersOpened.ContainsKey(order.Id);
        public bool IsCancelled(Order order) => order != null && order.Id > 0 && OrdersCancelled.ContainsKey(order.Id);
        public bool IsExecuted(Order order, out OrderExecution orderExecution)
        {
            orderExecution = null;
            if (order != null && order.Id > 0 && OrdersExecuted.ContainsKey(order.Id))
            {
                orderExecution = OrdersExecuted[order.Id];
                return true;
            }

            return false;
        }

        public void ValidateOrderPlacement(Order order)
        {
            if (order.Id > 0)
            {
                if (OrdersExecuted.ContainsKey(order.Id))
                    throw new ArgumentException($"This order ({order.Id}) has already been executed.");
                else if (OrdersCancelled.ContainsKey(order.Id))
                    throw new ArgumentException($"This order ({order.Id}) has already been cancelled.");
                else if (OrdersOpened.ContainsKey(order.Id))
                    throw new ArgumentException($"This order ({order.Id}) is already opened.");
                else if (OrdersRequested.ContainsKey(order.Id))
                    throw new ArgumentException($"This order ({order.Id}) has already been requested.");
            }
        }

        public void ValidateOrderModification(Order order)
        {
            if (order.Id < 0)
                throw new ArgumentException("Invalid order (order id not set).");
            else if (!OrdersOpened.ContainsKey(order.Id))
                throw new ArgumentException($"No opened order with id {order.Id} to modify.");
            if (OrdersExecuted.ContainsKey(order.Id))
                throw new ArgumentException($"This order ({order.Id}) has already been executed.");
            else if (OrdersCancelled.ContainsKey(order.Id))
                throw new ArgumentException($"This order ({order.Id}) has already been cancelled.");
        }

        public void ValidateOrderCancellation(int orderId)
        {
            if (orderId < 0)
                throw new ArgumentException("Invalid order Id (order id not set).");
            else if (!OrdersOpened.ContainsKey(orderId))
                throw new ArgumentException($"No opened order with id {orderId} to modify.");
            if (OrdersExecuted.ContainsKey(orderId))
                throw new ArgumentException($"This order ({orderId}) has already been executed.");
            else if (OrdersCancelled.ContainsKey(orderId))
                throw new ArgumentException($"This order ({orderId}) has already been cancelled.");
        }
    }
}
