using Broker.Accounts;
using Broker.IBKR.Client;
using NLog;

namespace Broker.IBKR.Orders
{
    internal class IBOrderManager : IOrderManager
    {
        ILogger? _logger;
        IBroker _broker;
        IBClient _client;
        OrderTracker _orderTracker;
        OrderValidator _validator;

        public IBOrderManager(IBBroker broker, ILogger? logger)
        {
            _logger = logger;
            _broker = broker;
            _client = broker.Client;
            _orderTracker = new OrderTracker();
            _validator = new OrderValidator(_orderTracker);

            _client.Responses.OpenOrder += OnOrderOpened;
            _client.Responses.OrderStatus += OnOrderStatus;
            _client.Responses.ExecDetails += OnOrderExecuted;
            _client.Responses.CommissionReport += OnCommissionInfo;
        }

        public event Action<string, Order, OrderStatus>? OrderUpdated;
        public event Action<string, OrderExecution>? OrderExecuted;

        // TODO : re-implement client-side order chains?
        public async Task<OrderPlacedResult> PlaceOrderAsync(string ticker, Order order) => await PlaceOrderAsync(ticker, order, CancellationToken.None);
        public async Task<OrderPlacedResult> PlaceOrderAsync(string ticker, Order order, CancellationToken token)
        {
            if (order.Id < 0)
                order.Id = await GetNextValidOrderIdAsync(token);
            else
                _validator.ValidateOrderPlacement(order);

            return await PlaceOrderInternalAsync(ticker, order, token);
        }

        public async Task<OrderPlacedResult> ModifyOrderAsync(Order order) => await ModifyOrderAsync(order, CancellationToken.None);
        public async Task<OrderPlacedResult> ModifyOrderAsync(Order order, CancellationToken token)
        {
            _validator.ValidateOrderModification(order);
            return await PlaceOrderInternalAsync(_orderTracker.OrderIdsToTicker[order.Id], order, token);
        }

        // TODO : investigate/implement/test partially filled orders
        async Task<OrderPlacedResult> PlaceOrderInternalAsync(string ticker, Order order, CancellationToken token)
        {
            var orderPlacedResult = new OrderPlacedResult();
            var tcs = new TaskCompletionSource<OrderPlacedResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            var contract = _client.ContractsCache.Get(ticker);

            if (order.Id < 0)
                order.Id = await GetNextValidOrderIdAsync(token);

            var openOrder = new Action<IBApi.Contract, IBApi.Order, IBApi.OrderState>((c, o, oState) =>
            {
                if (order.Id == o.OrderId)
                {
                    orderPlacedResult.Ticker = c.Symbol;
                    orderPlacedResult.Order = (Order)o;
                    orderPlacedResult.OrderState = (OrderState)oState;
                }
            });

            // With market orders, when the order is accepted and executes immediately, there commonly will not be any
            // corresponding orderStatus callbacks. For that reason it is recommended to also monitor the IBApi.EWrapper.execDetails .
            // NOTE : I wasn't able to reproduce this behavior with tests? Potential relevant discussion here : 
            // https://groups.io/g/twsapi/topic/trading_in_the_last_minute_of/79443776?p=,,,20,0,0,0::recentpostdate%2Fsticky,,,20,2,0,79443776
            var orderStatus = new Action<IBApi.OrderStatus>(os =>
            {
                var oStatus = (OrderStatus)os;
                if (order.Id == oStatus.Info.OrderId)
                {
                    if (oStatus.Status == Status.PreSubmitted || oStatus.Status == Status.Submitted)
                    {
                        orderPlacedResult.OrderStatus = oStatus;
                        orderPlacedResult.Time = DateTime.Now;
                        tcs.TrySetResult(orderPlacedResult);
                    }
                }
            });

            var error = new Action<ErrorMessageException>(msg =>
            {
                if (msg.ErrorMessage.Code == MessageCode.OrderMessageError && msg.Message.Contains("your order will not be placed at the exchange until"))
                    return;

                tcs.TrySetException(msg);
            });

            _client.Responses.OpenOrder += openOrder;
            _client.Responses.OrderStatus += orderStatus;
            _client.Responses.Error += error;

            try
            {
                _logger?.Info($"Placing order... : {order}");
                _orderTracker.TrackRequest(ticker, order);
                _client.PlaceOrder(contract, (IBApi.Order)order);
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.OpenOrder -= openOrder;
                _client.Responses.OrderStatus -= orderStatus;
                _client.Responses.Error -= error;
            }
        }

        public async Task<OrderStatus> CancelOrderAsync(int orderId) => await CancelOrderAsync(orderId, CancellationToken.None);
        public async Task<OrderStatus> CancelOrderAsync(int orderId, CancellationToken token)
        {
            _validator.ValidateOrderCancellation(orderId);
            var tcs = new TaskCompletionSource<OrderStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            var orderStatus = new Action<IBApi.OrderStatus>(os =>
            {
                var oStatus = (OrderStatus)os;
                if (orderId == oStatus.Info.OrderId)
                {
                    if (!tcs.Task.IsCompleted && (oStatus.Status == Status.ApiCancelled || oStatus.Status == Status.Cancelled))
                    {
                        _logger?.Info($"Order cancelled : {orderId}");
                        tcs.TrySetResult(oStatus);
                    }
                }
            });

            var error = new Action<ErrorMessageException>(msg =>
            {
                if (msg.ErrorMessage.Code == MessageCode.OrderCancelledCode) return;
                tcs.TrySetException(msg);
            });

            _client.Responses.OrderStatus += orderStatus;
            _client.Responses.Error += error;
            try
            {
                _logger?.Info($"Cancelling order... : {orderId}");
                _client.CancelOrder(orderId);
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.OrderStatus -= orderStatus;
                _client.Responses.Error -= error;
            }
        }

        public async Task<IEnumerable<OrderStatus>> CancelAllOrdersAsync() => await CancelAllOrdersAsync(CancellationToken.None);
        public async Task<IEnumerable<OrderStatus>> CancelAllOrdersAsync(CancellationToken token)
        {
            int nbRetries = 0;
            const int maxRetries = 5;
            IEnumerable<OrderStatus>? results = null;
            while (results == null && nbRetries < maxRetries)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    results = await CancelAllOrdersInternal(token);
                }
                catch (ErrorMessageException msg)
                {
                    if (msg.ErrorMessage.Code == MessageCode.OrderNotCancellableCode)
                    {
                        nbRetries++;
                        _logger?.Trace($"Retrying...{nbRetries}/{maxRetries}");
                    }
                    else
                        throw;
                }
            }

            return results!;
        }

        async Task<IEnumerable<OrderStatus>> CancelAllOrdersInternal(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<IEnumerable<OrderStatus>>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            var cancelledOrders = new Dictionary<int, OrderStatus>();

            IEnumerable<OrderPlacedResult> openOrders = await GetOpenOrdersAsync(token);
            var openOrdersCount = openOrders.Count();

            if (openOrdersCount == 0)
                return Enumerable.Empty<OrderStatus>();

            _logger?.Trace($"{openOrdersCount} currently open orders");
            var orderStatus = new Action<IBApi.OrderStatus>(os =>
            {
                var oStatus = (OrderStatus)os;
                _logger?.Trace($"{oStatus}");
                if (oStatus.Status == Status.Cancelled || oStatus.Status == Status.ApiCancelled)
                {
                    cancelledOrders[oStatus.Info.OrderId] = oStatus;
                }

                if (cancelledOrders.Count == openOrdersCount)
                {
                    _logger?.Info($"All open orders cancelled.");
                    tcs.TrySetResult(cancelledOrders.Values);
                }
            });

            var error = new Action<ErrorMessageException>(msg =>
            {
                if (msg.ErrorMessage.Code == MessageCode.OrderCancelledCode) return;
                tcs.TrySetException(msg);
            });

            _client.Responses.OrderStatus += orderStatus;
            _client.Responses.Error += error;

            try
            {
                _logger?.Info($"Cancelling all open orders...");
                _client.CancelAllOrders();
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.OrderStatus -= orderStatus;
                _client.Responses.Error -= error;
            }
        }

        public async Task<IEnumerable<OrderExecutedResult>> SellAllPositionsAsync() => await SellAllPositionsAsync(CancellationToken.None);
        public async Task<IEnumerable<OrderExecutedResult>> SellAllPositionsAsync(CancellationToken token)
        {
            var list = new List<OrderExecutedResult>();
            var account = await _broker.GetAccountAsync();
            foreach (KeyValuePair<string, Position> pos in account.Positions)
            {
                if (pos.Value.PositionAmount <= 0)
                    continue;

                var placedResult = await PlaceOrderAsync(pos.Key, new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = pos.Value.PositionAmount });
                if (placedResult?.Order != null)
                {
                    var execRes = await AwaitExecutionAsync(placedResult.Order);
                    list.Add(execRes);
                }
            }

            return list;
        }

        public async Task<OrderExecutedResult> AwaitExecutionAsync(Order order) => await AwaitExecutionAsync(order, CancellationToken.None);
        public async Task<OrderExecutedResult> AwaitExecutionAsync(Order order, CancellationToken token)
        {
            _validator.ValidateExecutionAwaiting(order.Id);
            if (_orderTracker.OrdersExecuted.ContainsKey(order.Id))
            {
                return new OrderExecutedResult()
                {
                    Ticker = _orderTracker.OrderIdsToTicker[order.Id],
                    Order = order,
                    OrderExecution = _orderTracker.OrdersExecuted[order.Id],
                };
            }

            var tcs = new TaskCompletionSource<OrderExecutedResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            var orderExecuted = new Action<string, OrderExecution>((ticker, oe) =>
            {
                if (oe.OrderId == order.Id)
                {
                    tcs.TrySetResult(new OrderExecutedResult()
                    {
                        Ticker = _orderTracker.OrderIdsToTicker[order.Id],
                        Order = order,
                        OrderExecution = _orderTracker.OrdersExecuted[order.Id],
                    });
                }
            });

            var orderUpdated = new Action<string, Order, OrderStatus>((ticker, o, os) =>
            {
                if (o.Id == order.Id && (os.Status == Status.Cancelled || os.Status == Status.ApiCancelled))
                    tcs.TrySetException(new Exception($"The order {order.Id} has been cancelled."));
            });

            OrderExecuted += orderExecuted;
            OrderUpdated += orderUpdated;
            try
            {
                _logger?.Debug($"Awaiting order execution... {order}");
                return await tcs.Task;
            }
            finally
            {
                OrderExecuted -= orderExecuted;
                OrderUpdated -= orderUpdated;
            }
        }

        // TODO : check if this callback is called when Order.Transmit == false
        void OnOrderOpened(IBApi.Contract c, IBApi.Order o, IBApi.OrderState s)
        {
            var ticker = c.Symbol;
            var order = (Order)o;
            var state = (OrderState)s;

            if (state.Status == Status.Submitted || state.Status == Status.PreSubmitted /*for paper trading accounts*/)
            {
                if (!_orderTracker.OrdersOpened.ContainsKey(order.Id)) // new order submitted
                {
                    _logger?.Info($"Order opened : {order}");
                    _orderTracker.TrackOpening(order);
                }
                else // modified order?
                {
                    _logger?.Info($"Order with id {order.Id} modified : {order}.");
                    _orderTracker.TrackOpening(order);
                }
            }

            var os = new OrderStatus()
            {
                Status = state.Status,
                Info = order.Info,
            };
            OrderUpdated?.Invoke(ticker, order, os);
        }

        void OnOrderStatus(IBApi.OrderStatus status)
        {
            OrderStatus os = (OrderStatus)status;
            if (!_orderTracker.OrdersOpened.TryGetValue(os.Info.OrderId, out Order? order))
                throw new ArgumentException($"Unknown order id {os.Info.OrderId}");

            if (os.Status == Status.Cancelled || os.Status == Status.ApiCancelled)
            {
                _logger?.Info($"OrderCancelled : {order}");
                _orderTracker.TrackCancellation(order);
            }

            _logger?.Debug($"Order status changed : {os}");
            OrderUpdated?.Invoke(_orderTracker.OrderIdsToTicker[os.Info.OrderId], order, os);
        }

        void OnOrderExecuted(IBApi.Contract contract, IBApi.Execution e)
        {
            var execution = (OrderExecution)e;
            _orderTracker.TrackExecution(execution);
            _logger?.Debug($"execution received : {execution}");
        }

        void OnCommissionInfo(IBApi.CommissionReport cr)
        {
            var ci = (CommissionInfo)cr;
            var exec = _orderTracker.ExecIdsToExecutions[ci.ExecId];
            exec.CommissionInfo = ci;

            _logger?.Debug($"commission report received : {ci}");
            _logger?.Info($"Order Executed : [{exec.OrderId}] {exec.Action} {exec.Shares} {exec.Price:c} avgPrice={exec.AvgPrice:c} commission={exec.CommissionInfo.Commission} time={exec.Time}");
            OrderExecuted?.Invoke(_orderTracker.OrderIdsToTicker[exec.OrderId], exec);
        }

        async Task<int> GetNextValidOrderIdAsync(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            var nextValidId = new Action<int>(id => tcs.SetResult(id));
            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _client.Responses.NextValidId += nextValidId;
            _client.Responses.Error += error;
            try
            {
                _logger?.Trace($"Requesting next valid order ids");
                _client.RequestValidOrderIds();
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.NextValidId -= nextValidId;
                _client.Responses.Error -= error;
            }
        }

        internal async Task<IEnumerable<OrderPlacedResult>> GetOpenOrdersAsync(CancellationToken token)
        {
            var orderPlacedResults = new Dictionary<int, OrderPlacedResult>();
            var tcs = new TaskCompletionSource<IEnumerable<OrderPlacedResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            var openOrder = new Action<IBApi.Contract, IBApi.Order, IBApi.OrderState>((c, o, oState) =>
            {
                if (!orderPlacedResults.ContainsKey(o.OrderId))
                    orderPlacedResults[o.OrderId] = new OrderPlacedResult();

                orderPlacedResults[o.OrderId].Ticker = c.Symbol;
                orderPlacedResults[o.OrderId].Order = (Order)o;
                orderPlacedResults[o.OrderId].OrderState = (OrderState)oState;
            });

            var orderStatus = new Action<IBApi.OrderStatus>(os =>
            {
                var oStatus = (OrderStatus)os;
                if (!orderPlacedResults.ContainsKey(oStatus.Info.OrderId))
                    orderPlacedResults[oStatus.Info.OrderId] = new OrderPlacedResult();

                orderPlacedResults[oStatus.Info.OrderId].OrderStatus = oStatus;
            });

            var openOrderEnd = new Action(() =>
            {
                IEnumerable<OrderPlacedResult> results = orderPlacedResults.Values.ToList();
                tcs.TrySetResult(results);
            });

            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _client.Responses.OpenOrder += openOrder;
            _client.Responses.OrderStatus += orderStatus;
            _client.Responses.OpenOrderEnd += openOrderEnd;
            _client.Responses.Error += error;

            try
            {
                _logger?.Debug($"Requesting open orders...");
                _client.RequestOpenOrders();
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.OpenOrder -= openOrder;
                _client.Responses.OrderStatus -= orderStatus;
                _client.Responses.OpenOrderEnd -= openOrderEnd;
                _client.Responses.Error -= error;
            }
        }
    }
}
