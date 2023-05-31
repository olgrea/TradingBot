namespace TradingBot.Broker.Orders
{
    internal class OrderValidator
    {
        OrderTracker _orderTracker;

        public OrderValidator(OrderTracker orderTracker)
        {
            _orderTracker = orderTracker;
        }

        internal void ValidateOrderPlacement(Order order)
        {
            if (_orderTracker.OrdersExecuted.ContainsKey(order.Id))
                throw new ArgumentException($"This order ({order.Id}) has already been executed.");
            else if (_orderTracker.OrdersCancelled.ContainsKey(order.Id))
                throw new ArgumentException($"This order ({order.Id}) has already been cancelled.");
            else if (_orderTracker.OpenOrders.ContainsKey(order.Id))
                throw new ArgumentException($"This order ({order.Id}) is already opened.");
            else if (_orderTracker.OrdersRequested.ContainsKey(order.Id))
                throw new ArgumentException($"This order ({order.Id}) has already been requested.");

            // TODO : add these client-side verifications? For Trader only? 
            // not enough funds when buying
            // not enough positions when selling
        }

        internal void ValidateOrderModification(Order order)
        {
            if (order.Id < 0)
                throw new ArgumentException("Invalid order (order id not set).");
            else if (_orderTracker.OrdersExecuted.ContainsKey(order.Id))
                throw new ArgumentException($"This order ({order.Id}) has already been executed.");
            else if (_orderTracker.OrdersCancelled.ContainsKey(order.Id))
                throw new ArgumentException($"This order ({order.Id}) has already been cancelled.");
            else if (!_orderTracker.OrdersRequested.ContainsKey(order.Id))
                throw new ArgumentException($"No open order with id {order.Id} to modify.");
        }

        internal void ValidateOrderCancellation(int orderId)
        {
            if (orderId < 0)
                throw new ArgumentException("Invalid order Id (order id not set).");
            else if (_orderTracker.OrdersExecuted.ContainsKey(orderId))
                throw new ArgumentException($"This order ({orderId}) has already been executed.");
            else if (_orderTracker.OrdersCancelled.ContainsKey(orderId))
                throw new ArgumentException($"This order ({orderId}) has already been cancelled.");
            else if (!_orderTracker.OpenOrders.ContainsKey(orderId) && _orderTracker.OrdersRequested.TryGetValue(orderId, out Order? o) && o.Info.Transmit)
                throw new ArgumentException($"No opened order with id {orderId} to modify.");
        }

        internal void ValidateExecutionAwaiting(int orderId)
        {
            if (orderId < 0)
                throw new ArgumentException("Invalid order Id (order id not set).");
            else if (!_orderTracker.OrdersExecuted.ContainsKey(orderId) && !_orderTracker.OpenOrders.ContainsKey(orderId))
                throw new ArgumentException($"No opened order with id {orderId} to await.");
            else if (_orderTracker.OrdersCancelled.ContainsKey(orderId))
                throw new ArgumentException($"This order ({orderId}) has already been cancelled.");
        }
    }
}
