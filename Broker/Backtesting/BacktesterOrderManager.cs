using System.Globalization;
using Broker.Accounts;
using Broker.IBKR.Orders;
using Broker.MarketData;
using Broker.Utils;

namespace Broker.Backtesting
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
            _validator = new OrderValidator(_orderTracker);

            _backtester = backtester;
            _backtester.ClockTick += OnClockTick_EvaluateConditions;
            _orderEvaluator = new OrderEvaluator(_backtester, _orderTracker);
            _orderEvaluator.OrderExecuted += OnOrderExecuted;
        }

        public event Action<string, Order, OrderStatus>? OrderUpdated;
        public event Action<string, OrderExecution>? OrderExecuted;

        int NextOrderId => _nextOrderId++;

        void OnClockTick_EvaluateConditions(DateTime newTime, CancellationToken token)
        {
            foreach (Order order in _orderTracker.OrdersRequested.Values.Where(o => o.NeedsConditionFulfillmentToBeOpened && !_orderTracker.HasBeenOpened(o)))
            {
                if (EvaluateConditions(order, newTime))
                {
                    order.Info.Transmit = true;
                    ModifyOrderInternal(order);
                }
            }

            foreach (Order order in _orderTracker.OrdersRequested.Values.Where(o => o.ConditionsTriggerOrderCancellation && !_orderTracker.IsCancelled(o)))
            {
                if (EvaluateConditions(order, newTime))
                {
                    CancelOrderInternal(order.Id, out _);
                }
            }
        }

        bool EvaluateConditions(Order order, DateTime time)
        {
            bool ret = true;
            bool previousOp = true;
            foreach (IBApi.OrderCondition condition in order.OrderConditions)
            {
                bool condResult = false;
                string ticker = _orderTracker.OrderIdsToTicker[order.Id];
                if (condition is IBApi.PriceCondition priceCond)
                {
                    IEnumerable<Last> lasts = _backtester.GetAsync<Last>(ticker, time).Result;
                    if (!lasts.Any())
                        condResult = false;
                    else if (priceCond.IsMore)
                        condResult = lasts.Any(l => priceCond.Price >= l.Price);
                    else
                        condResult = lasts.Any(l => priceCond.Price <= l.Price);
                }
                else if (condition is IBApi.PercentChangeCondition percentCond)
                {
                    var groups = _backtester.GetAsync<Last>(ticker, time.AddSeconds(-15), time).Result.GroupBy(l => l.Time);

                    condResult = false;
                    if (groups.Count() > 1)
                    {
                        var latestLasts = groups.Last();
                        var previousLasts = groups.SkipLast(1).Last();
                        if (percentCond.IsMore)
                            condResult = latestLasts.Any(l => previousLasts.Select(pl => l.Price / pl.Price - 1).Any(percent => percentCond.ChangePercent <= percent));
                        else
                            condResult = latestLasts.Any(l => previousLasts.Select(pl => l.Price / pl.Price - 1).Any(percent => percentCond.ChangePercent >= percent));
                    }
                }
                else if (condition is IBApi.TimeCondition timeCond)
                {
                    DateTime t = default;
                    try
                    {
                        t = DateTime.Parse(timeCond.Time);
                    }
                    catch (FormatException)
                    {
                        t = DateTime.ParseExact(timeCond.Time, MarketDataUtils.TWSTimeFormat, CultureInfo.InvariantCulture);
                    }

                    if (timeCond.IsMore)
                        condResult = t <= time;
                    else
                        condResult = t >= time;
                }

                ret = previousOp ? ret & condResult : ret | condResult;
                previousOp = condition.IsConjunctionConnection;
            }

            return ret;
        }

        public async Task<OrderPlacedResult> PlaceOrderAsync(string ticker, Order order) => await PlaceOrderAsync(ticker, order, CancellationToken.None);
        public async Task<OrderPlacedResult> PlaceOrderAsync(string ticker, Order order, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<OrderPlacedResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            Action request = new Action(async () =>
            {
                try
                {
                    OrderPlacedResult result = await PlaceOrderInternal(ticker, order);
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

        public async Task<OrderPlacedResult> ModifyOrderAsync(Order order) => await ModifyOrderAsync(order, CancellationToken.None);
        public async Task<OrderPlacedResult> ModifyOrderAsync(Order order, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<OrderPlacedResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            Action request = () =>
            {
                try
                {
                    OrderPlacedResult result = ModifyOrderInternal(order);
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

        public async Task<OrderStatus> CancelOrderAsync(int orderId) => await CancelOrderAsync(orderId, CancellationToken.None);
        public async Task<OrderStatus> CancelOrderAsync(int orderId, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<OrderStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            Action request = () =>
            {
                try
                {
                    OrderStatus os = CancelOrderInternal(orderId, out Order order);
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

        public async Task<IEnumerable<OrderStatus>> CancelAllOrdersAsync() => await CancelAllOrdersAsync(CancellationToken.None);
        public async Task<IEnumerable<OrderStatus>> CancelAllOrdersAsync(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<IEnumerable<OrderStatus>>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

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

        public async Task<OrderExecutedResult> AwaitExecutionAsync(Order order) => await AwaitExecutionAsync(order, CancellationToken.None);
        public async Task<OrderExecutedResult> AwaitExecutionAsync(Order order, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<OrderExecutedResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

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
                catch (Exception ex)
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

        public async Task<IEnumerable<OrderExecutedResult>> SellAllPositionsAsync() => await SellAllPositionsAsync(CancellationToken.None);
        public async Task<IEnumerable<OrderExecutedResult>> SellAllPositionsAsync(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<IEnumerable<OrderExecutedResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            var ordersPlaced = new Dictionary<int, Order>();
            Action request = () =>
            {
                try
                {
                    var positions = _backtester.Account.Positions.Where(p => p.Value.PositionAmount > 0);
                    if (!positions.Any())
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

        async Task<OrderPlacedResult> PlaceOrderInternal(string ticker, Order order)
        {
            order.Id = NextOrderId;
            _validator.ValidateOrderPlacement(order);
            _orderTracker.TrackRequest(ticker, order);

            // TODO : to test with TWS 
            if (order.Info.Transmit)
                _orderTracker.TrackOpening(order);

            foreach (var cond in order.OrderConditions.OfType<IBApi.ContractCondition>())
            {
                IBApi.Contract contract = await _backtester.GetContract(ticker);
                cond.ConId = contract.ConId;
                cond.Exchange = contract.Exchange;
            }

            var result = new OrderPlacedResult()
            {
                Order = order,
                OrderStatus = new OrderStatus()
                {
                    Status = order.Info.Transmit ? Status.Submitted : Status.Inactive,
                    Remaining = order.TotalQuantity,
                    Info = new RequestInfo()
                    {
                        OrderId = order.Id,
                        Transmit = order.Info.Transmit,
                    },
                },
                OrderState = new OrderState()
                {
                    Status = order.Info.Transmit ? Status.Submitted : Status.Inactive,
                },
                Time = _backtester.CurrentTime,
            };

            OrderUpdated?.Invoke(ticker, order, result.OrderStatus);
            return result;
        }

        OrderPlacedResult ModifyOrderInternal(Order order)
        {
            _validator.ValidateOrderModification(order);

            if (_orderTracker.OrdersRequested.ContainsKey(order.Id))
                _orderTracker.OrdersRequested[order.Id] = order;

            if (_orderTracker.OpenOrders.ContainsKey(order.Id) || order.Info.Transmit)
                _orderTracker.TrackOpening(order);

            var result = new OrderPlacedResult()
            {
                Order = order,
                OrderStatus = new OrderStatus()
                {
                    Status = order.Info.Transmit ? Status.Submitted : Status.Inactive,
                    Remaining = order.TotalQuantity,
                    Info = new RequestInfo()
                    {
                        OrderId = order.Id,
                    },
                },
                OrderState = new OrderState()
                {
                    Status = order.Info.Transmit ? Status.Submitted : Status.Inactive,
                },
                Time = _backtester.CurrentTime,
            };

            string ticker = _orderTracker.OrderIdsToTicker[order.Id];
            OrderUpdated?.Invoke(ticker, order, result.OrderStatus!);
            return result;
        }

        OrderStatus CancelOrderInternal(int orderId, out Order order)
        {
            _validator.ValidateOrderCancellation(orderId);

            order = _orderTracker.OrdersRequested[orderId];

            _orderTracker.TrackCancellation(order);

            OrderStatus os = new OrderStatus()
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
            return os;
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
