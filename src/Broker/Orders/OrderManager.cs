using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using TradingBot.Broker.Client;
using NLog;

namespace TradingBot.Broker.Orders
{
    internal class OrderManager
    {
        ILogger _logger;
        IBBroker _broker;
        IIBClient _client;

        Dictionary<int, Order> _ordersRequested = new Dictionary<int, Order>();
        Dictionary<int, OrderChain> _chainOrdersRequested = new Dictionary<int, OrderChain>();
        Dictionary<int, Order> _ordersSubmitted = new Dictionary<int, Order>();
        Dictionary<int, OrderStatus> _ordersFilled = new Dictionary<int, OrderStatus>();

        public OrderManager(IBBroker broker, IIBClient client, ILogger logger)
        {
            _logger = logger;
            _broker = broker;
            _client = client;

            _client.Callbacks.OpenOrder += OnOrderOpened;
            _client.Callbacks.OrderStatus += OnOrderStatus;
            _client.Callbacks.ExecDetails += OnOrderExecuted;
        }

        public event Action<OrderStatus, OrderExecution> OrderUpdated;

        public void PlaceOrder(Contract contract, Order order)
        {
            if (contract == null || order == null)
                return;

            order.Id = _broker.GetNextValidOrderId();

            Trace.Assert(!_ordersRequested.ContainsKey(order.Id));

            if (!order.Info.Transmit)
                _logger.Warn($"Order will not be submitted automatically since \"{nameof(order.Info.Transmit)}\" is set to false.");

            _ordersRequested[order.Id] = order;
            _client.PlaceOrder(contract, order);
        }

        public void PlaceOrder(Contract contract, OrderChain chain, bool useTWSAttachedOrderFeature = false)
        {
            if (contract == null || chain == null || chain.Order == null)
                return;

            // For some reasons in TWS, when an order becomes active, the quantity of all its attached orders gets set to the
            // quantity of the parent order regardless of what was set as quantity in children. This is undesirable in
            // some situations (ex : buy 100 shares but only put a stop loss on 50 shares).
            // I've kept the TWS mechanism but I implemented my own attached order mechanism to circumvent this limitation.
            if (useTWSAttachedOrderFeature)
            {
                PlaceTWSOrderChain(contract, chain);
                return;
            }
            
            if(chain.AttachedOrders.Any())
            {
                _logger.Debug($"Placing order {chain.Order} from order chain. {chain.AttachedOrders.Count} order(s) will be placed after the parent one gets filled.");
            }
            else
            {
                _logger.Debug($"Placing last order {chain.Order} from chain.");
            }

            PlaceOrder(contract, chain.Order);
            _chainOrdersRequested[chain.Order.Id] = chain;
        }

        void PlaceTWSOrderChain(Contract contract, OrderChain chain)
        {
            _logger.Debug("Placing order chain using TWS mechanism.");
            var list = AssignOrderIdsAndFlatten(chain);

            for (int i = 0; i < list.Count; i++)
            {
                Order o = list[i];

                // only the last child must be set to true to prevent parent orders from being
                // submitted (and possibly filled) before all orders are submitted.
                o.Info.Transmit = i == list.Count - 1;

                Trace.Assert(o.Id > 0 && !_ordersRequested.ContainsKey(o.Id));

                _ordersRequested[o.Id] = o;
                _client.PlaceOrder(contract, o);
            }
        }

        void OnOrderOpened(Contract contract, Order order, OrderState state)
        {
            if (state.Status == Status.Submitted || state.Status == Status.PreSubmitted /*for paper trading accounts*/)
            {
                if(!_ordersSubmitted.ContainsKey(order.Id)) // new order submitted
                {
                    _logger.Debug($"New order placed : {order}");
                    _ordersSubmitted[order.Id] = order;
                }
                else // modified order?
                {
                    _logger.Debug($"Order with id {order.Id} modified to : {order}.");
                    _ordersSubmitted[order.Id] = order;
                    if(_chainOrdersRequested.ContainsKey(order.Id))
                        _chainOrdersRequested[order.Id] = order;
                }

            }

            var os = new OrderStatus() 
            {
                Status = state.Status,
                Info = order.Info,
            };
            OrderUpdated?.Invoke(os, null);
        }

        void OnOrderStatus(OrderStatus os)
        {
            if (os.Status == Status.Cancelled || os.Status == Status.ApiCancelled)
            {
                if(_chainOrdersRequested.ContainsKey(os.Info.OrderId))
                {
                    _logger.Warn($"Order {os.Info.OrderId} was part of an order chain and has been cancelled. Attached orders will also be cancelled.");
                    _chainOrdersRequested.Remove(os.Info.OrderId);
                }

            }

            if(os.Status == Status.Filled)
                _ordersFilled.Add(os.Info.OrderId, os);
            
            OrderUpdated?.Invoke(os, null);
        }

        void OnOrderExecuted(Contract contract, OrderExecution execution)
        {
            if (_chainOrdersRequested.ContainsKey(execution.OrderId))
            {
                PlaceNextOrdersInChain(contract, _chainOrdersRequested[execution.OrderId]);
            }

            _ordersFilled.TryGetValue(execution.OrderId, out OrderStatus os);
            OrderUpdated?.Invoke(os, execution);
        }

        void PlaceNextOrdersInChain(Contract contract, OrderChain chain)
        {
            var executedOrder = chain.Order;
            _logger.Debug($"Order from chain executed : {executedOrder}");

            // First cancel all siblings, if any
            if (chain.Parent != null && _chainOrdersRequested.ContainsKey(chain.Parent.Id))
            {
                var parent = _chainOrdersRequested[chain.Parent.Id];
                _logger.Debug($"Cancelling {parent.AttachedOrders.Count} sibling orders.");
                foreach (OrderChain child in parent.AttachedOrders)
                {
                    if (child.Order.Id == executedOrder.Id)
                        continue;
                    
                    _chainOrdersRequested.Remove(child.Order.Id);
                    CancelOrder(child.Order);
                }
            }

            _logger.Debug($"Placing {chain.AttachedOrders.Count} attached orders.");
            // Then place all child of the executed order
            foreach (OrderChain child in chain.AttachedOrders)
            {
                PlaceOrder(contract, child);
            }
        }

        public void ModifyOrder(Contract contract, Order order)
        {
            Trace.Assert(order.Id > 0);
            if(!_ordersSubmitted.ContainsKey(order.Id))
            {
                throw new ArgumentException($"The order {order} hasn't been placed yet and therefore cannot be cancelled");
            }

            _logger.Debug($"Modifying order {order}.");
            _client.PlaceOrder(contract, order);
        }

        public void CancelOrder(Order order)
        {
            Trace.Assert(order.Id > 0);
            if (!_ordersSubmitted.ContainsKey(order.Id))
            {
                throw new ArgumentException($"The order {order} hasn't been placed and therefore cannot be cancelled");
            }

            _client.CancelOrder(order.Id);
        }

        List<Order> AssignOrderIdsAndFlatten(OrderChain chain, List<Order> list = null)
        {
            if (chain == null || chain.Order == null)
                return null;

            list ??= new List<Order>();

            chain.Order.Id = _broker.GetNextValidOrderId();
            list.Add(chain.Order);

            if (chain.AttachedOrders.Any())
            {
                int parentId = chain.Order.Id;

                int count = chain.AttachedOrders.Count;
                for (int i = 0; i < count; i++)
                {
                    var child = chain.AttachedOrders[i].Order;
                    child.Info.ParentId = parentId;
                    AssignOrderIdsAndFlatten(chain.AttachedOrders[i], list);
                }
            }

            return list;
        }
    }
}
