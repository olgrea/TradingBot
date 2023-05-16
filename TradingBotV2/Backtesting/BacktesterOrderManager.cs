using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.Orders;

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
                    OrderPlacedResult result = PlaceOrderInternal(ticker, order);
                    tcs.TrySetResult(result);
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            });

            var error = new Action<Exception>(e => tcs.TrySetException(e));
            _backtester.ErrorOccured += error;
            try
            {
                EnqueueRequest(tcs, request);
                return await tcs.Task;
            }
            finally
            {
                _backtester.ErrorOccured -= error;
            }
        }

        public async Task<OrderPlacedResult> ModifyOrderAsync(Order order)
        {
            var tcs = new TaskCompletionSource<OrderPlacedResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action request = () =>
            {
                try
                {
                    OrderPlacedResult result = ModifyOrderInternal(order);
                    string ticker = _orderTracker.OrderIdsToTicker[order.Id];
                    OrderUpdated?.Invoke(ticker, order, result.OrderStatus!);
                    tcs.TrySetResult(result);
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            };

            var error = new Action<Exception>(e => tcs.TrySetException(e));
            _backtester.ErrorOccured += error;
            try
            {
                EnqueueRequest(tcs, request);
                return await tcs.Task;
            }
            finally
            {
                _backtester.ErrorOccured -= error;
            }
        }
                
        public async Task<OrderStatus> CancelOrderAsync(int orderId)
        {
            var tcs = new TaskCompletionSource<OrderStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action request = () =>
            {
                try
                {
                    OrderStatus os = CancelOrderInternal(orderId, out Order order);
                    string ticker = _orderTracker.OrderIdsToTicker[order.Id];
                    OrderUpdated?.Invoke(ticker, order, os);
                    tcs.TrySetResult(os);
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            };

            var error = new Action<Exception>(e => tcs.TrySetException(e));
            _backtester.ErrorOccured += error;
            try
            {
                EnqueueRequest(tcs, request);
                return await tcs.Task;
            }
            finally
            {
                _backtester.ErrorOccured -= error;
            }
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

            var error = new Action<Exception>(e => tcs.TrySetException(e));
            _backtester.ErrorOccured += error;
            try
            {
                EnqueueRequest(tcs, request);
                return await tcs.Task;
            }
            finally
            {
                _backtester.ErrorOccured -= error;
            }
        }

        public async Task<OrderExecutedResult> AwaitExecutionAsync(Order order)
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
            var error = new Action<Exception>(e => tcs.TrySetException(e));
            
            _backtester.ErrorOccured += error;
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
                _backtester.ErrorOccured -= error;
            }
        }

        public async Task<IEnumerable<OrderExecutedResult>> SellAllPositionsAsync()
        {
            var tcs = new TaskCompletionSource<IEnumerable<OrderExecutedResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ordersPlaced = new Dictionary<int, Order>();
            Action request = () =>
            {
                try
                {
                    var positions = _backtester.Account.Positions.Where(p => p.Value.PositionAmount > 0);
                    if(!positions.Any())
                        tcs.TrySetResult(Enumerable.Empty<OrderExecutedResult>());

                    foreach (KeyValuePair<string, Position> pos in positions)
                    {
                        MarketOrder marketOrder = new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = pos.Value.PositionAmount };
                        var result = PlaceOrderInternal(pos.Key, marketOrder);
                        ordersPlaced.Add(marketOrder.Id, marketOrder);
                    }
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            };

            var execList = new List<OrderExecutedResult>();
            var orderExecuted = new Action<string, OrderExecution>((ticker, oe) =>
            {
                if (ordersPlaced.ContainsKey(oe.OrderId))
                {
                    execList.Add(new OrderExecutedResult()
                    {
                        Ticker = _orderTracker.OrderIdsToTicker[oe.OrderId],
                        Order = ordersPlaced[oe.OrderId],
                        OrderExecution = oe,
                    });

                    if (execList.Count == ordersPlaced.Count)
                        tcs.TrySetResult(execList);
                }
            });
            var error = new Action<Exception>(e => tcs.TrySetException(e));

            _backtester.ErrorOccured += error;
            OrderExecuted += orderExecuted;
            try
            {
                EnqueueRequest(tcs, request);
                return await tcs.Task;
            }
            finally
            {
                _backtester.ErrorOccured -= error;
                OrderExecuted -= orderExecuted;
            }
        }

        OrderPlacedResult PlaceOrderInternal(string ticker, Order order)
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
                },
                Time = _backtester.CurrentTime,
            };

            OrderUpdated?.Invoke(ticker, order, result.OrderStatus);
            return result;
        }

        OrderPlacedResult ModifyOrderInternal(Order order)
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
                },
                Time = _backtester.CurrentTime,
            };
            return result;
        }

        OrderStatus CancelOrderInternal(int orderId, out Order order)
        {
            _validator.ValidateOrderCancellation(orderId);

            order = _orderTracker.OpenOrders[orderId];
            _orderTracker.TrackCancellation(order);

            return new OrderStatus()
            {
                Status = Status.Cancelled,
                Remaining = order.TotalQuantity,
                Info = new RequestInfo()
                {
                    OrderId = order.Id,
                },
            };
        }

        void EnqueueRequest<TResult>(TaskCompletionSource<TResult> tcs, Action request)
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
