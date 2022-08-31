using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using TradingBot.Utils;
using TradingBot.Broker.Orders;

namespace TradingBot.Broker.Client
{
    public class TWSOrderManager
    {
        ILogger _logger;
        TWSClient _client;

        Dictionary<int, Order> _ordersRequested = new Dictionary<int, Order>();
        Dictionary<int, Order> _ordersSubmitted = new Dictionary<int, Order>();

        public TWSOrderManager(TWSClient client, ILogger logger)
        {
            _logger = logger;
            _client = client;
            _client.OrderOpened += OnOrderOpened;
        }

        public Action<Contract, Order, OrderState> OrderOpened;
        public Action<Contract, Order, OrderState> OrderStatusChanged;
        public Action<Contract, Order, OrderState> OrderExecuted;

        public void PlaceOrder(Contract contract, Order order)
        {
            if (contract == null || order == null)
                return;

            var list = AssignOrderIdsAndFlatten(order);

            for (int i = 0; i < list.Count; i++)
            {
                Order o = list[i];

                // only the last child must be set to true.
                o.Request.Transmit = i == list.Count - 1;

                Debug.Assert(o.Request.OrderId > 0 && !_ordersRequested.ContainsKey(o.Request.OrderId));

                _ordersRequested[o.Request.OrderId] = o;
                _client.PlaceOrder(contract, o);
            }
        }

        void OnOrderOpened(Contract contract, Order order, OrderState state)
        {
            //if(state.Status == Status.PreSubmitted)
            //{
            //    _ordersSubmitted[order.Request.OrderId] = order;
            //}

            //if (state.Status == Status.Filled)
            //{
            //    FixChildrenOrderQuantity(contract, order, state);
            //}


        }

        void FixChildrenOrderQuantity(Contract contract, Order order, OrderState state)
        {
            if(!_ordersRequested.ContainsKey(order.Request.OrderId))
                return;

            // For some reasons in TWS, when an order becomes active, the quantity of all its child orders gets set to the
            // quantity of the parent regardless of what was set as quantity in children.
            // This is undesirable. Example : I may not want to put a stop loss for all the shares I just bought. Just half.
            var requestedOrder = _ordersRequested[order.Request.OrderId];

            if(requestedOrder.Request.AttachedOrders.Any())
            {
                foreach (var child in requestedOrder.Request.AttachedOrders)
                {
                    if(_ordersSubmitted.ContainsKey(child.Request.OrderId))
                    {
                        var os = _ordersSubmitted[child.Request.OrderId];
                        if (child.TotalQuantity != os.TotalQuantity)
                        {
                            os.TotalQuantity = child.TotalQuantity;
                            
                            // Re-Submit order
                            _client.PlaceOrder(contract, child);
                        }
                    }
                }
            }
        }

        public void ModifyOrder(Contract contract, Order order)
        {
            Debug.Assert(order.Request.OrderId > 0);
        }

        public void CancelOrder(Order order)
        {
            Debug.Assert(order.Request.OrderId > 0);
        }

        List<Order> AssignOrderIdsAndFlatten(Order order, List<Order> list = null)
        {
            if (order == null)
                return null;
            list ??= new List<Order>();

            order.Request.OrderId = _client.GetNextValidOrderId();
            list.Add(order);

            if (order.Request.AttachedOrders.Any())
            {
                int parentId = order.Request.OrderId;

                int count = order.Request.AttachedOrders.Count;
                for (int i = 0; i < count; i++)
                {
                    var child = order.Request.AttachedOrders[i];
                    child.Request.ParentId = parentId;
                    AssignOrderIdsAndFlatten(child, list);
                }
            }

            return list;
        }
    }
}
