using System.Diagnostics;
using System.Net.Sockets;
using NLog;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.Orders;

namespace TradingBotV2.IBKR
{
    internal class IBOrderManager : IOrderManager
    {
        ILogger _logger;
        IBClient _client;

        IDictionary<int, string> _orderIdsToTicker = new Dictionary<int, string>(); 
        IDictionary<int, Order> _ordersRequested = new Dictionary<int, Order>();
        IDictionary<int, Order> _ordersOpened = new Dictionary<int, Order>();
        IDictionary<int, OrderExecution> _ordersExecuted = new Dictionary<int, OrderExecution>();
        IDictionary<int, Order> _ordersCancelled = new Dictionary<int, Order>();
        IDictionary<string, OrderExecution> _executions = new Dictionary<string, OrderExecution>();

        public IBOrderManager(IBClient client, ILogger logger)
        {
            _logger = logger;
            _client = client;

            _client.Responses.OpenOrder += OnOrderOpened;
            _client.Responses.OrderStatus += OnOrderStatus;
            _client.Responses.ExecDetails += OnOrderExecuted;
            _client.Responses.CommissionReport += OnCommissionInfo;
        }

        public event Action<string, Order, OrderStatus> OrderUpdated;
        public event Action<string, OrderExecution, CommissionInfo> OrderExecuted;

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

        public async Task<OrderResult> PlaceOrderAsync(string ticker, Order order)
        {
            if (order.Id > 0)
            {
                if (_ordersExecuted.ContainsKey(order.Id))
                    throw new ArgumentException($"This order ({order.Id}) has already been executed.");
                else if (_ordersCancelled.ContainsKey(order.Id))
                    throw new ArgumentException($"This order ({order.Id}) has already been cancelled.");
                else if (_ordersOpened.ContainsKey(order.Id))
                    throw new ArgumentException($"This order ({order.Id}) is already opened.");
                else if (_ordersRequested.ContainsKey(order.Id))
                    throw new ArgumentException($"This order ({order.Id}) has already been requested.");
            }

            return await PlaceOrderInternalAsync(ticker, order);
        }

        public async Task<OrderResult> ModifyOrderAsync(Order order)
        {
            if (order.Id < 0)
                throw new ArgumentException("Invalid order (order id not set).");
            else if (!_ordersOpened.ContainsKey(order.Id))
                throw new ArgumentException($"No opened order with id {order.Id} to modify.");
            if (_ordersExecuted.ContainsKey(order.Id))
                throw new ArgumentException($"This order ({order.Id}) has already been executed.");
            else if (_ordersCancelled.ContainsKey(order.Id))
                throw new ArgumentException($"This order ({order.Id}) has already been cancelled.");

            return await PlaceOrderInternalAsync(_orderIdsToTicker[order.Id], order);
        }

        public async Task<OrderStatus> CancelOrderAsync(int orderId)
        {
            if (orderId < 0)
                throw new ArgumentException("Invalid order Id (order id not set).");
            else if (!_ordersOpened.ContainsKey(orderId))
                throw new ArgumentException($"No opened order with id {orderId} to modify.");
            if (_ordersExecuted.ContainsKey(orderId))
                throw new ArgumentException($"This order ({orderId}) has already been executed.");
            else if (_ordersCancelled.ContainsKey(orderId))
                throw new ArgumentException($"This order ({orderId}) has already been cancelled.");

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
                _orderIdsToTicker[order.Id] = ticker;
                _ordersRequested[order.Id] = order;
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
                if (!_ordersOpened.ContainsKey(order.Id)) // new order submitted
                {
                    _logger.Debug($"New order placed : {order}");
                    _ordersOpened[order.Id] = order;
                }
                else // modified order?
                {
                    _logger.Debug($"Order with id {order.Id} modified to : {order}.");
                    _ordersOpened[order.Id] = order;
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
            _ordersOpened.TryGetValue(os.Info.OrderId, out Order order);
            if (os.Status == Status.Cancelled || os.Status == Status.ApiCancelled)
            {
                _ordersCancelled.TryAdd(os.Info.OrderId, order);
            }

            OrderUpdated?.Invoke(_orderIdsToTicker[order.Id], order, os);
        }

        void OnOrderExecuted(IBApi.Contract contract, IBApi.Execution e)
        {
            var execution = (OrderExecution)e;
            _ordersExecuted.TryAdd(execution.OrderId, execution);
            _executions.TryAdd(execution.ExecId, execution);
        }

        void OnCommissionInfo(IBApi.CommissionReport cr)
        {
            var ci = (CommissionInfo)cr;
            var exec = _executions[ci.ExecId];
            OrderExecuted?.Invoke(_orderIdsToTicker[exec.OrderId], exec, ci);
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
                _logger.Debug($"Requesting next valid order ids");
                _client.RequestValidOrderIds();
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.NextValidId -= nextValidId;
                _client.Responses.Error -= error;
            }
        }
    }
}
