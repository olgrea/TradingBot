using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using TradingBot.Utils;
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

        public OrderManager(IBBroker broker, IIBClient client, ILogger logger)
        {
            _logger = logger;
            _broker = broker;
            _client = client;

            _client.Callbacks.OpenOrder += OnOrderOpened;
            _client.Callbacks.ExecDetails += OnOrderExecuted;
        }

        public void PlaceOrder(Contract contract, Order order)
        {
            if (contract == null || order == null)
                return;

            order.Id = _broker.GetNextValidOrderId();

            Trace.Assert(!_ordersRequested.ContainsKey(order.Id));

            if (!order.RequestInfo.Transmit)
                _logger.Warn($"Order will not be submitted automatically since \"{nameof(order.RequestInfo.Transmit)}\" is set to false.");

            _ordersRequested[order.Id] = order;
            _client.PlaceOrder(contract, order);
        }

        public void PlaceOrder(Contract contract, OrderChain chain, bool useTWSAttachedOrderFeature = false)
        {
            //TODO : add a bajillion logs everywhere

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

            PlaceOrder(contract, chain.Order);
            _chainOrdersRequested[chain.Order.Id] = chain;
        }

        void PlaceTWSOrderChain(Contract contract, OrderChain chain)
        {
            var list = AssignOrderIdsAndFlatten(chain);

            for (int i = 0; i < list.Count; i++)
            {
                Order o = list[i];

                // only the last child must be set to true to prevent parent orders from being
                // submitted (and possibly filled) before all orders are submitted.
                o.RequestInfo.Transmit = i == list.Count - 1;

                Trace.Assert(o.Id > 0 && !_ordersRequested.ContainsKey(o.Id));

                _ordersRequested[o.Id] = o;
                _client.PlaceOrder(contract, o);
            }
        }

        void OnOrderOpened(Contract contract, Order order, OrderState state)
        {
            if (state.Status == Status.Submitted || state.Status == Status.PreSubmitted /*for paper trading accounts*/)
            {
                _ordersSubmitted[order.Id] = order;
            }
        }

        void OnOrderExecuted(Contract contract, OrderExecution execution)
        {
            if (_chainOrdersRequested.ContainsKey(execution.OrderId))
            {
                PlaceNextOrdersInChain(contract, _chainOrdersRequested[execution.OrderId]);
            }
        }

        void PlaceNextOrdersInChain(Contract contract, OrderChain chain)
        {
            var executedOrder = chain.Order;

            // First cancel all siblings, if any
            if (chain.Parent != null && _chainOrdersRequested.ContainsKey(chain.Parent.Id))
            {
                var parent = _chainOrdersRequested[chain.Parent.Id];
                foreach (OrderChain child in parent.AttachedOrders)
                {
                    if (child.Order.Id == executedOrder.Id)
                        continue;
                    CancelOrder(child.Order);
                    _chainOrdersRequested.Remove(child.Order.Id);
                }
            }

            // Then place all child of the executed order
            foreach (OrderChain child in chain.AttachedOrders)
            {
                PlaceOrder(contract, child);
            }
        }

        public void ModifyOrder(Contract contract, Order order)
        {
            Trace.Assert(order.Id > 0);
            _client.PlaceOrder(contract, order);
        }

        public void CancelOrder(Order order)
        {
            Trace.Assert(order.Id > 0);
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
                    child.RequestInfo.ParentId = parentId;
                    AssignOrderIdsAndFlatten(chain.AttachedOrders[i], list);
                }
            }

            return list;
        }
    }
}
