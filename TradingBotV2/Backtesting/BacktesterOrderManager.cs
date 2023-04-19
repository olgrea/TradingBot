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
        BacktesterLiveDataProvider _dataProvider;

        int _nextOrderId = 1;

        // TODO : use a dependency injecton framework

        public BacktesterOrderManager(Backtester backtester)
        {
            _orderTracker = new OrderTracker();
            _validator = new OrderValidator(backtester, _orderTracker);
            
            _backtester = backtester;
            _dataProvider = _backtester.LiveDataProvider as BacktesterLiveDataProvider;
            _backtester.ClockTick += OnClockTick_EvaluateOrders;

            _orderEvaluator = new OrderEvaluator(_backtester, this);
        }

        public event Action<string, Order, OrderStatus> OrderUpdated;
        public event Action<string, OrderExecution> OrderExecuted
        {
            add => _orderEvaluator.OrderExecuted += value;
            remove => _orderEvaluator.OrderExecuted -= value;
        }

        int NextOrderId => _nextOrderId++;

        private void OnClockTick_EvaluateOrders(DateTime newTime)
        {
            foreach(Order o in _orderTracker.OpenOrders.Values)
            {
                var ticker = _orderTracker.OrderIdsToTicker[o.Id];
                var marketData = _dataProvider.GetMarketData(ticker);

                IEnumerable<BidAsk> latestBidAsks = null;
                var current = newTime;
                while (!marketData.BidAsks.ContainsKey(current))
                    current = current.AddSeconds(-1);
                latestBidAsks = marketData.BidAsks[current];

                foreach(BidAsk bidAsk in latestBidAsks)
                    _orderEvaluator.EvaluateOrder(ticker, o, bidAsk);
            }
        }

        public async Task<OrderResult> PlaceOrderAsync(string ticker, Order order)
        {
            var tcs = new TaskCompletionSource<OrderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
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

        public async Task<OrderResult> ModifyOrderAsync(Order order)
        {
            var tcs = new TaskCompletionSource<OrderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
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
    }
}
