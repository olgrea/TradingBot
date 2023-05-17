using NLog;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.Orders;

namespace TradingBotV2.Backtesting
{
    internal class OrderEvaluator
    {
        Backtester _backtester;
        OrderTracker _orderTracker;
        ILogger? _logger;
        int _execId = 1;

        public OrderEvaluator(Backtester backtester, OrderTracker orderTracker)
        {
            _backtester = backtester;
            _logger = backtester.Logger;
            _orderTracker = orderTracker;

            _backtester.ClockTick += OnClockTick_EvaluateOrders;
            PositionUpdated += p => _backtester.OnPositionUpdated(p);
        }

        int NextExecId => _execId++;
        internal event Action<string, OrderExecution>? OrderExecuted;
        internal event Action<Position>? PositionUpdated;

        void OnClockTick_EvaluateOrders(DateTime newTime, CancellationToken token)
        {
            foreach (Order o in _orderTracker.OpenOrders.Values)
            {
                token.ThrowIfCancellationRequested();
                var ticker = _orderTracker.OrderIdsToTicker[o.Id];
                
                IEnumerable<BidAsk> latestBidAsks = _backtester.GetAsync<BidAsk>(ticker, newTime, token).Result;
                foreach (BidAsk bidAsk in latestBidAsks)
                {
                    token.ThrowIfCancellationRequested();

                    if (_orderTracker.OpenOrders.ContainsKey(o.Id))
                        EvaluateOrder(ticker, o, bidAsk);
                }
            }
        }

        void EvaluateOrder(string ticker, Order order, BidAsk bidAsk)
        {
            _logger?.Debug($"Evaluating Order {order} at BidAsk : {bidAsk}");

            if (order is MarketOrder mo)
            {
                EvaluateMarketOrder(ticker, bidAsk, mo);
            }
            else if (order is LimitOrder lo)
            {
                EvaluateLimitOrder(ticker, bidAsk, lo);
            }
            else if (order is StopOrder so)
            {
                EvaluateStopOrder(ticker, bidAsk, so);
            }
            else if (order is TrailingStopOrder tso)
            {
                EvaluateTrailingStopOrder(ticker, bidAsk, tso);
            }
            else if (order is MarketIfTouchedOrder mito)
            {
                EvaluateMarketIfTouchedOrder(ticker, bidAsk, mito);
            }
        }

        private void EvaluateMarketOrder(string ticker, BidAsk bidAsk, MarketOrder o)
        {
            if (o.Action == OrderAction.BUY)
            {
                ExecuteOrder(ticker, o, bidAsk.Ask);
            }
            else if (o.Action == OrderAction.SELL)
            {
                ExecuteOrder(ticker, o, bidAsk.Bid);
            }
        }

        private void EvaluateMarketIfTouchedOrder(string ticker, BidAsk bidAsk, MarketIfTouchedOrder o)
        {
            if (o.Action == OrderAction.BUY && o.TouchPrice >= bidAsk.Bid)
            {
                _logger?.Debug($"{o.OrderType} {o.Action} Order : touch price of {o.TouchPrice:c} reached. {bidAsk}");
                ExecuteOrder(ticker, o, bidAsk.Ask);
            }
            else if (o.Action == OrderAction.SELL && o.TouchPrice <= bidAsk.Ask)
            {
                _logger?.Debug($"{o.OrderType} {o.Action} Order : touch price of {o.TouchPrice:c} reached. {bidAsk}");
                ExecuteOrder(ticker, o, bidAsk.Bid);
            }
        }

        private void EvaluateTrailingStopOrder(string ticker, BidAsk bidAsk, TrailingStopOrder o)
        {
            ArgumentNullException.ThrowIfNull(o.TrailingAmount, nameof(o.TrailingAmount));
            ArgumentNullException.ThrowIfNull(o.TrailingAmountUnits, nameof(o.TrailingAmountUnits));

            if (o.Action == OrderAction.BUY)
            {
                if (o.StopPrice is null)
                {
                    if (o.TrailingAmountUnits == TrailingAmountUnits.Percent)
                        o.StopPrice = bidAsk.Ask * (1 + o.TrailingAmount);
                    else if (o.TrailingAmountUnits == TrailingAmountUnits.Absolute)
                        o.StopPrice = bidAsk.Ask  + o.TrailingAmount;
                }

                if (o.StopPrice <= bidAsk.Ask)
                {
                    _logger?.Debug($"{o} : stop price of {o.StopPrice:c} reached. Ask : {bidAsk.Ask:c}");
                    ExecuteOrder(ticker, o, bidAsk.Ask);
                }
                else if (o.TrailingAmountUnits == TrailingAmountUnits.Percent)
                {
                    var currentPercent = (o.StopPrice - bidAsk.Ask) / bidAsk.Ask;
                    if (currentPercent > o.TrailingAmount)
                    {
                        var newVal = bidAsk.Ask + o.TrailingAmount * bidAsk.Ask;
                        _logger?.Trace($"{o} : current%={currentPercent} trail%={o.TrailingAmount} : adjusting stop price of {o.StopPrice:c} to {newVal:c}");
                        o.StopPrice = newVal;
                    }
                }
                else if (o.TrailingAmountUnits == TrailingAmountUnits.Absolute)
                {
                    // The price must be updated if the ask falls
                    var currentStopPrice = bidAsk.Ask + o.TrailingAmount;
                    if (currentStopPrice < o.StopPrice)
                    {
                        o.StopPrice = currentStopPrice;
                        _logger?.Trace($"{o} : adjusting stop price of {o.StopPrice:c} to {currentStopPrice:c}");
                    }
                }
            }
            else if (o.Action == OrderAction.SELL)
            {
                if (o.StopPrice is null)
                {
                    if (o.TrailingAmountUnits == TrailingAmountUnits.Percent)
                        o.StopPrice = bidAsk.Bid * (1 - o.TrailingAmount);
                    else if (o.TrailingAmountUnits == TrailingAmountUnits.Absolute)
                        o.StopPrice = bidAsk.Bid - o.TrailingAmount;
                }

                if (o.StopPrice >= bidAsk.Bid)
                {
                    _logger?.Debug($"{o} : stop price of {o.StopPrice:c} reached. Bid: {bidAsk.Bid:c}");
                    ExecuteOrder(ticker, o, bidAsk.Bid);
                }
                else if (o.TrailingAmountUnits == TrailingAmountUnits.Percent)
                {
                    var currentPercent = (o.StopPrice - bidAsk.Bid) / -bidAsk.Bid;
                    if (currentPercent > o.TrailingAmount)
                    {
                        var newVal = bidAsk.Bid - o.TrailingAmount * bidAsk.Bid;
                        _logger?.Trace($"{o} : current%={currentPercent} trail%={o.TrailingAmount} : adjusting stop price of {o.StopPrice:c} to {newVal:c}");
                        o.StopPrice = newVal;
                    }
                }
                else if (o.TrailingAmountUnits == TrailingAmountUnits.Absolute)
                {
                    // The price must be updated if the bid rises
                    var currentStopPrice = bidAsk.Bid - o.TrailingAmount;
                    if (currentStopPrice > o.StopPrice)
                    {
                        o.StopPrice = currentStopPrice;
                        _logger?.Trace($"{o} : adjusting stop price of {o.StopPrice:c} to {currentStopPrice:c}");
                    }
                }
            }
        }

        private void EvaluateStopOrder(string ticker, BidAsk bidAsk, StopOrder o)
        {
            if (o.Action == OrderAction.BUY && o.StopPrice <= bidAsk.Ask)
            {
                _logger?.Debug($"{o} : stop price of {o.StopPrice:c} reached. {bidAsk}");
                ExecuteOrder(ticker, o, bidAsk.Ask);
            }
            else if (o.Action == OrderAction.SELL && o.StopPrice >= bidAsk.Bid)
            {
                _logger?.Debug($"{o} : stop price of {o.StopPrice:c} reached. {bidAsk}");
                ExecuteOrder(ticker, o, bidAsk.Bid);
            }
        }

        private void EvaluateLimitOrder(string ticker, BidAsk bidAsk, LimitOrder o)
        {
            if (o.Action == OrderAction.BUY && o.LmtPrice >= bidAsk.Ask)
            {
                _logger?.Debug($"{o} : lmt price of {o.LmtPrice:c} reached. Ask : {bidAsk.Ask:c}");
                ExecuteOrder(ticker, o, bidAsk.Ask);
            }
            else if (o.Action == OrderAction.SELL && o.LmtPrice <= bidAsk.Bid)
            {
                _logger?.Debug($"{o} : lmt price of {o.LmtPrice:c} reached. Bid : {bidAsk.Bid:c}");
                ExecuteOrder(ticker, o, bidAsk.Bid);
            }
        }

        void ExecuteOrder(string ticker, Order order, double price)
        {
            _logger?.Info($"{order} : Executing at price {price:c}");
            var total = order.TotalQuantity * price;

            Account account = _backtester.Account;
            if(!_backtester.Account.Positions.TryGetValue(ticker, out Position? position))
            {
                position = _backtester.Account.Positions[ticker] = new Position(ticker);
            }

            if (order.Action == OrderAction.BUY)
            {
                // TODO implement on client-side
                if (total > _backtester.Account.CashBalances["USD"])
                {
                    _logger?.Error($"{order} Cannot execute BUY order! Not enough funds (required : {total}, actual : {account.CashBalances["USD"]}");

                    //CancelOrder(order.Id);
                    return;
                }

                position.AverageCost = position.PositionAmount != 0 ? (position.AverageCost + price) / 2 : price;
                position.PositionAmount += order.TotalQuantity;

                _logger?.Debug($"Account {account.Code} :  New position {position.PositionAmount} at {position.AverageCost:c}/shares");

                _backtester.UpdateUnrealizedPNL(ticker, price);
                _backtester.UpdateCashBalance(-total);
            }
            else if (order.Action == OrderAction.SELL)
            {
                // TODO implement on client-side
                if (position.PositionAmount < order.TotalQuantity)
                {
                    _logger?.Error($"{order} Cannot execute SELL order! Not enough position (required : {order.TotalQuantity}, actual : {position.PositionAmount}");
                    //CancelOrderInternal(order.Id);
                    return;
                }

                position.PositionAmount -= order.TotalQuantity;
                _logger?.Debug($"Account {account.Code} :  New position {position.PositionAmount} at {position.AverageCost:c}/shares");

                _backtester.UpdateRealizedPNL(ticker, order.TotalQuantity, price);
                _backtester.UpdateUnrealizedPNL(ticker, price);
                _backtester.UpdateCashBalance(total);
            }

            double commission = _backtester.UpdateCommissions(order, price);

            _logger?.Debug($"Account {account.Code} :  New USD cash balance : {account.CashBalances["USD"]:c}");

            string nextExecId = NextExecId.ToString();
            var oe = new OrderExecution(nextExecId, order.Id)
            {
                AcctNumber = account.Code,
                Time = _backtester.CurrentTime,
                Action = order.Action,
                Shares = order.TotalQuantity,
                Price = total,
                AvgPrice = price,
                CommissionInfo = new CommissionInfo(nextExecId)
                {
                    Commission = commission,
                    Currency = "USD",
                    RealizedPNL = position.RealizedPNL
                }
            };

            OrderExecuted?.Invoke(ticker, oe);
            PositionUpdated?.Invoke(position);

            _backtester.SendAccountUpdates();
        }
    }
}
