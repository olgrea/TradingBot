using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using NLog;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using InteractiveBrokers.Orders;
using InteractiveBrokers;
using InteractiveBrokers.Contracts;
using InteractiveBrokers.Messages;
using System.Threading;
using System.Net.Sockets;

namespace TradingBot
{
    // TODO : move that to IBClient ?
    internal class OrderManager
    {
        ILogger _logger;
        IBClient _client;

        ConcurrentDictionary<int, Order> _ordersRequested = new ConcurrentDictionary<int, Order>();
        ConcurrentDictionary<int, OrderChain> _chainOrdersRequested = new ConcurrentDictionary<int, OrderChain>();
        ConcurrentDictionary<int, Order> _ordersOpened = new ConcurrentDictionary<int, Order>();
        ConcurrentDictionary<int, OrderExecution> _ordersExecuted = new ConcurrentDictionary<int, OrderExecution>();
        ConcurrentDictionary<int, Order> _ordersCancelled = new ConcurrentDictionary<int, Order>();
        ConcurrentDictionary<string, OrderExecution> _executions = new ConcurrentDictionary<string, OrderExecution>();

        public OrderManager(IBClient broker, ILogger logger)
        {
            _logger = logger;
            _client = broker;

            _client.OrderOpened += OnOrderOpened;
            _client.OrderStatusChanged += OnOrderStatus;
            _client.OrderExecuted += OnOrderExecuted;
            _client.CommissionInfoReceived += OnCommissionInfo;
        }

        public event Action<Order, OrderStatus> OrderUpdated;
        public event Action<OrderExecution, CommissionInfo> OrderExecuted;

        public bool HasBeenRequested(Order order) => order != null && order.Id > 0 && _ordersRequested.ContainsKey(order.Id);
        public bool HasBeenOpened(Order order) => order != null && order.Id > 0 && _ordersOpened.ContainsKey(order.Id);
        public bool IsCancelled(Order order) => order != null && order.Id > 0 && _ordersCancelled.ContainsKey(order.Id);
        public bool IsExecuted(Order order, out OrderExecution orderExecution)
        {
            orderExecution = null;
            if (order != null && order.Id > 0 && _ordersExecuted.ContainsKey(order.Id))
            {
                orderExecution = _ordersExecuted[order.Id];
                return true;
            }

            return false;
        }

        public async Task<OrderResult> PlaceOrderAsync(Contract contract, Order order, bool waitForExecution = false)
        {
            CancellationTokenSource source = new CancellationTokenSource(Debugger.IsAttached ? -1 : 5000);
            return await PlaceOrderAsync(contract, order, source.Token, waitForExecution);
        }

        // TODO : add param waitForExecution ?
        public async Task<OrderResult> PlaceOrderAsync(Contract contract, Order order, CancellationToken token, bool waitForExecution = false)
        {
            order.Id = await _client.GetNextValidOrderIdAsync(token);

            Trace.Assert(!_ordersRequested.ContainsKey(order.Id));

            if (!order.Info.Transmit)
                _logger.Warn($"Order will not be submitted automatically since \"{nameof(order.Info.Transmit)}\" is set to false.");

            _ordersRequested[order.Id] = order;

            if (!waitForExecution)
                return await _client.PlaceOrderAsync(contract, order, token);
            
            return await PlaceAndWaitForExecution(contract, order, token);
        }

        async Task<OrderResult> PlaceAndWaitForExecution(Contract contract, Order order, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<OrderResult>();
            var orderExecutedResult = new OrderExecutedResult() { Contract = contract };

            var execDetails = new Action<Contract, OrderExecution>((c, oe) =>
            {
                if (order.Id == oe.OrderId)
                    orderExecutedResult.OrderExecution = oe;
            });

            var commissionReport = new Action<CommissionInfo>(ci =>
            {
                if (orderExecutedResult.OrderExecution.ExecId == ci.ExecId)
                {
                    orderExecutedResult.CommissionInfo = ci;
                    tcs.TrySetResult(orderExecutedResult);
                }
            });

            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _client.OrderExecuted += execDetails;
            _client.CommissionInfoReceived += commissionReport;
            _ = tcs.Task.ContinueWith(t =>
            {
                _client.OrderExecuted -= execDetails;
                _client.CommissionInfoReceived -= commissionReport;
            });

            OrderResult res = await _client.PlaceOrderAsync(contract, order, token);
            orderExecutedResult.Order = res.Order;

            return await tcs.Task;
        }

        public async Task<IEnumerable<OrderResult>> PlaceOrderAsync(Contract contract, OrderChain chain, bool useTWSAttachedOrderFeature = false)
        {
            CancellationTokenSource source = new CancellationTokenSource(Debugger.IsAttached ? -1 : 5000);
            return await PlaceOrderAsync(contract, chain, source.Token, useTWSAttachedOrderFeature);
        }

        public async Task<IEnumerable<OrderResult>> PlaceOrderAsync(Contract contract, OrderChain chain, CancellationToken token, bool useTWSAttachedOrderFeature = false)
        {
            // For some reasons in TWS, when an order becomes active, the quantity of all its attached orders gets set to the
            // quantity of the parent order regardless of what was set as quantity in children. This is undesirable in
            // some situations (ex : buy 100 shares but only put a stop loss on 50 shares).
            // I've kept the TWS mechanism but I implemented my own attached order mechanism to circumvent this limitation.
            if (useTWSAttachedOrderFeature)
            {
                return await PlaceTWSOrderChain(contract, chain);
            }

            if (chain.AttachedOrders.Any())
            {
                _logger.Debug($"Placing order {chain.Order} from order chain. {chain.AttachedOrders.Count} order(s) will be placed after the parent one gets filled.");
            }
            else
            {
                _logger.Debug($"Placing last order {chain.Order} from chain.");
            }

            var r = await PlaceOrderAsync(contract, chain.Order);
            
            _chainOrdersRequested[chain.Order.Id] = chain;
            return new[] { r };
        }

        async Task<IEnumerable<OrderResult>> PlaceTWSOrderChain(Contract contract, OrderChain chain)
        {
            _logger.Debug("Placing order chain using TWS mechanism.");
            List<Order> list = await AssignOrderIdsAndFlatten(chain);

            List<OrderResult> results = new List<OrderResult>();
            for (int i = 0; i < list.Count; i++)
            {
                Order o = list[i];

                // only the last child must be set to true to prevent parent orders from being
                // submitted (and possibly filled) before all orders are submitted.
                o.Info.Transmit = i == list.Count - 1;

                Trace.Assert(o.Id > 0 && !_ordersRequested.ContainsKey(o.Id));

                _ordersRequested[o.Id] = o;
                var r = await _client.PlaceOrderAsync(contract, o);
                results.Add(r);
            }

            return results;
        }

        void OnOrderOpened(Contract contract, Order order, OrderState state)
        {
            if (state.Status == Status.Submitted || state.Status == Status.PreSubmitted /*for paper trading accounts*/)
            {
                if (!_ordersOpened.ContainsKey(order.Id)) // new order submitted
                {
                    _logger.Debug($"New order placed : {order}");
                    _ordersOpened[order.Id] = order;
                }
                else // modified order?
                {
                    _logger.Debug($"Order with id {order.Id} modified to : {order}.");
                    _ordersOpened[order.Id] = order;
                    if (_chainOrdersRequested.ContainsKey(order.Id))
                        _chainOrdersRequested[order.Id] = order;
                }

            }

            var os = new OrderStatus()
            {
                Status = state.Status,
                Info = order.Info,
            };
            OrderUpdated?.Invoke(order, os);
        }

        void OnOrderStatus(OrderStatus os)
        {
            _ordersOpened.TryGetValue(os.Info.OrderId, out Order order);
            if (os.Status == Status.Cancelled || os.Status == Status.ApiCancelled)
            {
                if (_chainOrdersRequested.ContainsKey(os.Info.OrderId))
                {
                    _logger.Warn($"Order {os.Info.OrderId} was part of an order chain and has been cancelled. Attached orders will also be cancelled.");
                    _chainOrdersRequested.TryRemove(os.Info.OrderId, out _);
                }
                _ordersCancelled.TryAdd(os.Info.OrderId, order);
            }

            OrderUpdated?.Invoke(order, os);
        }

        void OnOrderExecuted(Contract contract, OrderExecution execution)
        {
            if (_chainOrdersRequested.ContainsKey(execution.OrderId))
            {
                PlaceNextOrdersInChain(contract, _chainOrdersRequested[execution.OrderId]);
            }

            _ordersExecuted.TryAdd(execution.OrderId, execution);
            _executions.TryAdd(execution.ExecId, execution);
        }

        void OnCommissionInfo(CommissionInfo ci)
        {
            var exec = _executions[ci.ExecId];
            OrderExecuted?.Invoke(exec, ci);
        }

        async void PlaceNextOrdersInChain(Contract contract, OrderChain chain)
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

                    _chainOrdersRequested.TryRemove(child.Order.Id, out _);
                    await CancelOrderAsync(child.Order);
                }
            }

            _logger.Debug($"Placing {chain.AttachedOrders.Count} attached orders.");
            // Then place all child of the executed order
            foreach (OrderChain child in chain.AttachedOrders)
            {
                await PlaceOrderAsync(contract, child);
            }
        }

        public async Task<OrderResult> ModifyOrderAsync(Contract contract, Order order)
        {
            Trace.Assert(order.Id > 0);
            if (!_ordersOpened.ContainsKey(order.Id))
            {
                throw new ArgumentException($"The order {order} hasn't been placed yet and therefore cannot be modified");
            }

            _logger.Debug($"Modifying order {order}.");
            return await _client.PlaceOrderAsync(contract, order);
        }

        public async Task<OrderStatus> CancelOrderAsync(Order order)
        {
            Trace.Assert(order.Id > 0);
            if (!_ordersOpened.ContainsKey(order.Id))
            {
                throw new ArgumentException($"The order {order} hasn't been placed and therefore cannot be cancelled");
            }

            return await _client.CancelOrderAsync(order.Id);
        }

        public void CancelAllOrders()
        {
            _chainOrdersRequested.Clear();
            _client.CancelAllOrders();
        }

        async Task<List<Order>> AssignOrderIdsAndFlatten(OrderChain chain, List<Order> list = null)
        {
            if (chain == null || chain.Order == null)
                return null;

            list ??= new List<Order>();

            chain.Order.Id = await _client.GetNextValidOrderIdAsync();
            list.Add(chain.Order);

            if (chain.AttachedOrders.Any())
            {
                int parentId = chain.Order.Id;

                int count = chain.AttachedOrders.Count;
                for (int i = 0; i < count; i++)
                {
                    var child = chain.AttachedOrders[i].Order;
                    child.Info.ParentId = parentId;
                    await AssignOrderIdsAndFlatten(chain.AttachedOrders[i], list);
                }
            }

            return list;
        }
    }
}
