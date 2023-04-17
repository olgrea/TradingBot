using NLog;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.Orders;

namespace TradingBotV2.IBKR
{
    internal class IBOrderManager : IOrderManager
    {
        ILogger _logger;
        IBClient _client;
        OrderTracker _orderTracker;

        public IBOrderManager(IBClient client, ILogger logger)
        {
            _logger = logger;
            _client = client;
            _orderTracker = new OrderTracker();

            _client.Responses.OpenOrder += OnOrderOpened;
            _client.Responses.OrderStatus += OnOrderStatus;
            _client.Responses.ExecDetails += OnOrderExecuted;
            _client.Responses.CommissionReport += OnCommissionInfo;
        }

        public event Action<string, Order, OrderStatus> OrderUpdated;
        public event Action<string, OrderExecution, CommissionInfo> OrderExecuted;

        public async Task<OrderResult> PlaceOrderAsync(string ticker, Order order)
        {
            _orderTracker.ValidateOrderPlacement(order);
            return await PlaceOrderInternalAsync(ticker, order);
        }

        public async Task<OrderResult> ModifyOrderAsync(Order order)
        {
            _orderTracker.ValidateOrderModification(order);
            return await PlaceOrderInternalAsync(_orderTracker.OrderIdsToTicker[order.Id], order);
        }

        public async Task<OrderStatus> CancelOrderAsync(int orderId)
        {
            _orderTracker.ValidateOrderCancellation(orderId);
            var tcs = new TaskCompletionSource<OrderStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            var orderStatus = new Action<IBApi.OrderStatus>(os =>
            {
                var oStatus = (OrderStatus)os;
                if (orderId == oStatus.Info.OrderId)
                {
                    if (!tcs.Task.IsCompleted && (oStatus.Status == Status.ApiCancelled || oStatus.Status == Status.Cancelled))
                        tcs.TrySetResult(oStatus);
                }
            });

            var error = new Action<ErrorMessage>(msg => tcs.TrySetException(msg));

            _client.Responses.OrderStatus += orderStatus;
            _client.Responses.Error += error;
            try
            {
                _client.CancelOrder(orderId);
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.OrderStatus -= orderStatus;
                _client.Responses.Error -= error;
            }
        }

        public async Task<IEnumerable<OrderStatus>> CancelAllOrdersAsync()
        {
            var tcs = new TaskCompletionSource<IEnumerable<OrderStatus>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelledOrders = new List<OrderStatus>();

            var openOrdersCount = (await GetOpenOrdersAsync()).Count();
            if (openOrdersCount == 0)
                return Enumerable.Empty<OrderStatus>();

            var orderStatus = new Action<IBApi.OrderStatus>(os =>
            {
                var oStatus = (OrderStatus)os;
                if (oStatus.Status == Status.Cancelled || oStatus.Status == Status.ApiCancelled)
                {
                    cancelledOrders.Add(oStatus);
                }

                if (cancelledOrders.Count == openOrdersCount)
                    tcs.TrySetResult(cancelledOrders);
            });

            var error = new Action<ErrorMessage>(msg =>
            {
                if (msg.ErrorCode == 202) // Order cancelled
                    return;

                tcs.TrySetException(msg);
            });

            _client.Responses.OrderStatus += orderStatus;
            _client.Responses.Error += error;

            try
            {
                _client.CancelAllOrders();
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.OrderStatus -= orderStatus;
                _client.Responses.Error -= error;
            }
        }

        // TODO : investigate/implement/test partially filled orders

        async Task<OrderResult> PlaceOrderInternalAsync(string ticker, Order order)
        {
            var orderPlacedResult = new OrderPlacedResult();
            var tcs = new TaskCompletionSource<OrderResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            var contract = await _client.ContractsCache.GetAsync(ticker);

            if (order.Id < 0)
                order.Id = await GetNextValidOrderIdAsync();

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
                if (order.Id == oStatus.Info.OrderId && (oStatus.Status == Status.PreSubmitted || oStatus.Status == Status.Submitted))
                {
                    orderPlacedResult.OrderStatus = oStatus;
                    tcs.TrySetResult(orderPlacedResult);
                }
            });

            var error = new Action<ErrorMessage>(msg =>
            {
                if (!MarketDataUtils.IsMarketOpen() && msg.ErrorCode == 399 && msg.Message.Contains("your order will not be placed at the exchange until"))
                    return;

                tcs.TrySetException(msg);
            });

            _client.Responses.OpenOrder += openOrder;
            _client.Responses.OrderStatus += orderStatus;
            _client.Responses.Error += error;

            try
            {
                _orderTracker.OrderIdsToTicker[order.Id] = ticker;
                _orderTracker.OrdersRequested[order.Id] = order;
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

        void OnOrderOpened(IBApi.Contract c, IBApi.Order o, IBApi.OrderState s)
        {
            var ticker = c.Symbol;
            var order = (Order)o;
            var state = (OrderState)s;

            if (state.Status == Status.Submitted || state.Status == Status.PreSubmitted /*for paper trading accounts*/)
            {
                if (!_orderTracker.OrdersOpened.ContainsKey(order.Id)) // new order submitted
                {
                    _logger?.Debug($"New order placed : {order}");
                    _orderTracker.OrdersOpened[order.Id] = order;
                }
                else // modified order?
                {
                    _logger?.Debug($"Order with id {order.Id} modified to : {order}.");
                    _orderTracker.OrdersOpened[order.Id] = order;
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
            _orderTracker.OrdersOpened.TryGetValue(os.Info.OrderId, out Order order);
            if (os.Status == Status.Cancelled || os.Status == Status.ApiCancelled)
            {
                _orderTracker.OrdersCancelled.TryAdd(os.Info.OrderId, order);
            }

            OrderUpdated?.Invoke(_orderTracker.OrderIdsToTicker[order.Id], order, os);
        }

        void OnOrderExecuted(IBApi.Contract contract, IBApi.Execution e)
        {
            var execution = (OrderExecution)e;
            _orderTracker.OrdersExecuted.TryAdd(execution.OrderId, execution);
            _orderTracker.Executions.TryAdd(execution.ExecId, execution);
        }

        void OnCommissionInfo(IBApi.CommissionReport cr)
        {
            var ci = (CommissionInfo)cr;
            var exec = _orderTracker.Executions[ci.ExecId];
            OrderExecuted?.Invoke(_orderTracker.OrderIdsToTicker[exec.OrderId], exec, ci);
        }

        async Task<int> GetNextValidOrderIdAsync()
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            var nextValidId = new Action<int>(id => tcs.SetResult(id));
            var error = new Action<ErrorMessage>(msg => tcs.TrySetException(msg));

            _client.Responses.NextValidId += nextValidId;
            _client.Responses.Error += error;
            try
            {
                _logger?.Debug($"Requesting next valid order ids");
                _client.RequestValidOrderIds();
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.NextValidId -= nextValidId;
                _client.Responses.Error -= error;
            }
        }

        internal async Task<IEnumerable<OrderResult>> GetOpenOrdersAsync()
        {
            var orderPlacedResults = new Dictionary<int, OrderPlacedResult>();
            var tcs = new TaskCompletionSource<IEnumerable<OrderResult>>(TaskCreationOptions.RunContinuationsAsynchronously);

            var openOrder = new Action<IBApi.Contract, IBApi.Order, IBApi.OrderState>((c, o, oState) =>
            {
                if(!orderPlacedResults.ContainsKey(o.OrderId))
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
                IEnumerable<OrderResult> results = orderPlacedResults.Values.ToList();
                tcs.TrySetResult(results);
            });

            var error = new Action<ErrorMessage>(msg =>
            {
                if (!MarketDataUtils.IsMarketOpen() && msg.ErrorCode == 399 && msg.Message.Contains("your order will not be placed at the exchange until"))
                    return;

                tcs.TrySetException(msg);
            });

            _client.Responses.OpenOrder += openOrder;
            _client.Responses.OrderStatus += orderStatus;
            _client.Responses.OpenOrderEnd += openOrderEnd;
            _client.Responses.Error += error;

            try
            {
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
