using System.Net.Sockets;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.Orders;
using TradingBotV2.IBKR;

namespace TradingBotV2.Backtesting
{
    internal class BacktesterOrderManager : IOrderManager
    {
        OrderEvaluator _orderEvaluator;
        OrderTracker _orderTracker;
        OrderValidator _validator;
        Backtester _backtester;

        int _nextOrderId = 1;

        // TODO : use a dependency injecton framework

        public BacktesterOrderManager(Backtester backtester)
        {
            _orderTracker = new OrderTracker();
            _validator = new OrderValidator(backtester, _orderTracker);
            
            _backtester = backtester;
            _orderEvaluator = new OrderEvaluator(_backtester, _orderTracker);
            _orderEvaluator.OrderExecuted += OnOrderExecuted;
        }

        public event Action<string, Order, OrderStatus>? OrderUpdated;
        public event Action<string, OrderExecution>? OrderExecuted;

        int NextOrderId => _nextOrderId++;

        public async Task<OrderPlacedResult> PlaceOrderAsync(string ticker, Order order)
        {
            var tcs = new TaskCompletionSource<OrderPlacedResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action request = new Action(() =>
            {
                try
                {
                    order.Id = NextOrderId;
                    _validator.ValidateOrderPlacement(order);
                    _orderTracker.TrackRequest(ticker, order);
                    _orderTracker.TrackOpening(order);

                    var result = new OrderPlacedResult()
                    {
                        Order = order,
                        OrderStatus = new OrderStatus()
                        {
                            Status = Status.Submitted,
                            Remaining = order.TotalQuantity,
                            Info = new RequestInfo()
                            {
                                OrderId = order.Id,
                            },
                        },
                        OrderState = new OrderState()
                        {
                            Status = Status.Submitted,
                        }
                    };

                    OrderUpdated?.Invoke(ticker, order, result.OrderStatus);
                    tcs.TrySetResult(result);
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            });

            EnqueueRequest(tcs, request);

            return await tcs.Task;
        }

        public async Task<OrderPlacedResult> ModifyOrderAsync(Order order)
        {
            var tcs = new TaskCompletionSource<OrderPlacedResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action request = () =>
            {
                try
                {
                    _validator.ValidateOrderModification(order);
                    _orderTracker.TrackOpening(order);

                    var result = new OrderPlacedResult()
                    {
                        Order = order,
                        OrderStatus = new OrderStatus()
                        {
                            Status = Status.Submitted,
                            Remaining = order.TotalQuantity,
                            Info = new RequestInfo()
                            {
                                OrderId = order.Id,
                            },
                        },
                        OrderState = new OrderState()
                        {
                            Status = Status.Submitted,
                        }
                    };

                    string ticker = _orderTracker.OrderIdsToTicker[order.Id];
                    OrderUpdated?.Invoke(ticker, order, result.OrderStatus);
                    tcs.TrySetResult(result);
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            };

            EnqueueRequest(tcs, request);

            return await tcs.Task;
        }

        public async Task<OrderStatus> CancelOrderAsync(int orderId)
        {
            var tcs = new TaskCompletionSource<OrderStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action request = () =>
            {
                try
                {
                    _validator.ValidateOrderCancellation(orderId);

                    var order = _orderTracker.OpenOrders[orderId];
                    _orderTracker.TrackCancellation(order);

                    var os = new OrderStatus()
                    {
                        Status = Status.Cancelled,
                        Remaining = order.TotalQuantity,
                        Info = new RequestInfo()
                        {
                            OrderId = order.Id,
                        },
                    };

                    string ticker = _orderTracker.OrderIdsToTicker[order.Id];
                    OrderUpdated?.Invoke(ticker, order, os);
                    tcs.TrySetResult(os);
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            };

            EnqueueRequest(tcs, request);

            return await tcs.Task;
        }

        public async Task<IEnumerable<OrderStatus>> CancelAllOrdersAsync()
        {
            var tcs = new TaskCompletionSource<IEnumerable<OrderStatus>>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action request = () =>
            {
                var list = new List<OrderStatus>();
                foreach (Order order in _orderTracker.OpenOrders.Values)
                {
                    var os = new OrderStatus()
                    {
                        Status = Status.Cancelled,
                        Remaining = order.TotalQuantity,
                        Info = new RequestInfo()
                        {
                            OrderId = order.Id,
                        },
                    };

                    string ticker = _orderTracker.OrderIdsToTicker[order.Id];
                    OrderUpdated?.Invoke(ticker, order, os);

                    list.Add(os);
                }

                tcs.TrySetResult(list);
            };

            EnqueueRequest(tcs, request);

            return await tcs.Task;
        }

        public async Task<OrderExecutedResult> AwaitExecution(Order order)
        {
            var tcs = new TaskCompletionSource<OrderExecutedResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action request = () =>
            {
                try
                {
                    _validator.ValidateExecutionAwaiting(order.Id);
                    if (_orderTracker.OrdersExecuted.ContainsKey(order.Id))
                    {
                        tcs.TrySetResult(new OrderExecutedResult()
                        {
                            Ticker = _orderTracker.OrderIdsToTicker[order.Id],
                            Order = order,
                            OrderExecution = _orderTracker.OrdersExecuted[order.Id],
                        });
                    }
                }
                catch(Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            };

            var orderExecuted = new Action<string, OrderExecution>((ticker, oe) =>
            {
                if (oe.OrderId == order.Id)
                {
                    tcs.TrySetResult(new OrderExecutedResult()
                    {
                        Ticker = _orderTracker.OrderIdsToTicker[order.Id],
                        Order = order,
                        OrderExecution = oe,
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
                EnqueueRequest(tcs, request);
                return await tcs.Task;
            }
            finally
            {
                OrderExecuted -= orderExecuted;
                OrderUpdated -= orderUpdated;
            }
        }

        private void EnqueueRequest<TResult>(TaskCompletionSource<TResult> tcs, Action request)
        {
            try
            {
                _backtester.EnqueueRequest(request);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                    tcs.TrySetCanceled();
                else
                    tcs.TrySetException(ex);
            }
        }

        void OnOrderExecuted(string ticker, OrderExecution execution)
        {
            _orderTracker.TrackExecution(execution);
            OrderExecuted?.Invoke(ticker, execution);
        }
    }
}
