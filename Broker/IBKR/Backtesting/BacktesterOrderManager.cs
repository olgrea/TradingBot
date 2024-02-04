using System.Globalization;
using Broker.Accounts;
using Broker.IBKR.Orders;
using Broker.MarketData;
using Broker.Orders;
using Broker.Utils;

namespace Broker.IBKR.Backtesting
{
    internal class BacktesterOrderManager : IOrderManager<IBOrder>
    {
        OrderEvaluator _orderEvaluator;
        IBOrderTracker _orderTracker;
        IBOrderValidator _validator;
        Backtester _backtester;

        int _nextOrderId = 1;

        // TODO : use a dependency injecton framework

        public BacktesterOrderManager(Backtester backtester)
        {
            _orderTracker = new IBOrderTracker();
            _validator = new IBOrderValidator(_orderTracker);

            _backtester = backtester;
            _backtester.ClockTick += OnClockTick_EvaluateConditions;
            _orderEvaluator = new OrderEvaluator(_backtester, _orderTracker);
            _orderEvaluator.OrderExecuted += OnOrderExecuted;
        }

        public event Action<string, IBOrder, IBOrderStatus>? OrderUpdated;
        public event Action<string, IBOrderExecution>? OrderExecuted;

        int NextOrderId => _nextOrderId++;

        void OnClockTick_EvaluateConditions(DateTime newTime, CancellationToken token)
        {
            foreach (IBOrder order in _orderTracker.OrdersRequested.Values.Where(o => o.NeedsConditionFulfillmentToBeOpened && !_orderTracker.HasBeenOpened(o)))
            {
                if (EvaluateConditions(order, newTime))
                {
                    order.Info.Transmit = true;
                    ModifyOrderInternal(order);
                }
            }

            foreach (IBOrder order in _orderTracker.OrdersRequested.Values.Where(o => o.ConditionsTriggerOrderCancellation && !_orderTracker.IsCancelled(o)))
            {
                if (EvaluateConditions(order, newTime))
                {
                    CancelOrderInternal(order.Id, out _);
                }
            }
        }

        bool EvaluateConditions(IBOrder order, DateTime time)
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

        public async Task<IOrderResult> PlaceOrderAsync(string ticker, IBOrder order) => await PlaceOrderAsync(ticker, order, CancellationToken.None);
        public async Task<IOrderResult> PlaceOrderAsync(string ticker, IBOrder order, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<IBOrderPlacedResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            Action request = new Action(async () =>
            {
                try
                {
                    IBOrderPlacedResult result = await PlaceOrderInternal(ticker, order);
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

        public async Task<IOrderResult> ModifyOrderAsync(IBOrder order) => await ModifyOrderAsync(order, CancellationToken.None);
        public async Task<IOrderResult> ModifyOrderAsync(IBOrder order, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<IBOrderPlacedResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            Action request = () =>
            {
                try
                {
                    IBOrderPlacedResult result = ModifyOrderInternal(order);
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

        public async Task<IBOrderStatus> CancelOrderAsync(int orderId) => await CancelOrderAsync(orderId, CancellationToken.None);
        public async Task<IBOrderStatus> CancelOrderAsync(int orderId, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<IBOrderStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            Action request = () =>
            {
                try
                {
                    IBOrderStatus os = CancelOrderInternal(orderId, out IBOrder order);
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

        public async Task<IEnumerable<IBOrderStatus>> CancelAllOrdersAsync() => await CancelAllOrdersAsync(CancellationToken.None);
        public async Task<IEnumerable<IBOrderStatus>> CancelAllOrdersAsync(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<IEnumerable<IBOrderStatus>>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            Action request = () =>
            {
                var list = new List<IBOrderStatus>();
                foreach (IBOrder order in _orderTracker.OpenOrders.Values)
                {
                    var os = new IBOrderStatus()
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

        public async Task<IOrderResult> AwaitExecutionAsync(IBOrder order) => await AwaitExecutionAsync(order, CancellationToken.None);
        public async Task<IOrderResult> AwaitExecutionAsync(IBOrder order, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<IBOrderExecutedResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            Action request = () =>
            {
                try
                {
                    _validator.ValidateExecutionAwaiting(order.Id);
                    if (_orderTracker.OrdersExecuted.ContainsKey(order.Id))
                    {
                        tcs.TrySetResult(new IBOrderExecutedResult()
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

            var orderExecuted = new Action<string, IBOrderExecution>((ticker, oe) =>
            {
                if (oe.OrderId == order.Id)
                {
                    tcs.TrySetResult(new IBOrderExecutedResult()
                    {
                        Ticker = _orderTracker.OrderIdsToTicker[order.Id],
                        Order = order,
                        OrderExecution = oe,
                    });
                }
            });

            var orderUpdated = new Action<string, IBOrder, IBOrderStatus>((ticker, o, os) =>
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

        public async Task<IEnumerable<IOrderResult>> SellAllPositionsAsync() => await SellAllPositionsAsync(CancellationToken.None);
        public async Task<IEnumerable<IOrderResult>> SellAllPositionsAsync(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<IEnumerable<IBOrderExecutedResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            var ordersPlaced = new Dictionary<int, IBOrder>();
            Action request = () =>
            {
                try
                {
                    var positions = _backtester.Account.Positions.Where(p => p.Value.PositionAmount > 0);
                    if (!positions.Any())
                        tcs.TrySetResult(Enumerable.Empty<IBOrderExecutedResult>());

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

            var execList = new List<IBOrderExecutedResult>();
            var orderExecuted = new Action<string, IBOrderExecution>((ticker, oe) =>
            {
                if (ordersPlaced.ContainsKey(oe.OrderId))
                {
                    execList.Add(new IBOrderExecutedResult()
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

        async Task<IBOrderPlacedResult> PlaceOrderInternal(string ticker, IBOrder order)
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

            var result = new IBOrderPlacedResult()
            {
                Order = order,
                OrderStatus = new IBOrderStatus()
                {
                    Status = order.Info.Transmit ? Status.Submitted : Status.Inactive,
                    Remaining = order.TotalQuantity,
                    Info = new RequestInfo()
                    {
                        OrderId = order.Id,
                        Transmit = order.Info.Transmit,
                    },
                },
                OrderState = new IBOrderState()
                {
                    Status = order.Info.Transmit ? Status.Submitted : Status.Inactive,
                },
                Time = _backtester.CurrentTime,
            };

            OrderUpdated?.Invoke(ticker, order, result.OrderStatus);
            return result;
        }

        IBOrderPlacedResult ModifyOrderInternal(IBOrder order)
        {
            _validator.ValidateOrderModification(order);

            if (_orderTracker.OrdersRequested.ContainsKey(order.Id))
                _orderTracker.OrdersRequested[order.Id] = order;

            if (_orderTracker.OpenOrders.ContainsKey(order.Id) || order.Info.Transmit)
                _orderTracker.TrackOpening(order);

            var result = new IBOrderPlacedResult()
            {
                Order = order,
                OrderStatus = new IBOrderStatus()
                {
                    Status = order.Info.Transmit ? Status.Submitted : Status.Inactive,
                    Remaining = order.TotalQuantity,
                    Info = new RequestInfo()
                    {
                        OrderId = order.Id,
                    },
                },
                OrderState = new IBOrderState()
                {
                    Status = order.Info.Transmit ? Status.Submitted : Status.Inactive,
                },
                Time = _backtester.CurrentTime,
            };

            string ticker = _orderTracker.OrderIdsToTicker[order.Id];
            OrderUpdated?.Invoke(ticker, order, result.OrderStatus!);
            return result;
        }

        IBOrderStatus CancelOrderInternal(int orderId, out IBOrder order)
        {
            _validator.ValidateOrderCancellation(orderId);

            order = _orderTracker.OrdersRequested[orderId];

            _orderTracker.TrackCancellation(order);

            IBOrderStatus os = new IBOrderStatus()
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

        void OnOrderExecuted(string ticker, IBOrderExecution execution)
        {
            _orderTracker.TrackExecution(execution);
            OrderExecuted?.Invoke(ticker, execution);
        }
    }
}
