using NLog;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.Orders;

namespace TradingBotV2.Backtesting
{
    internal class OrderEvaluator
    {
        Backtester _backtester;
        BacktesterOrderManager _orderManager;
        ILogger _logger;

        public event Action<string, OrderExecution> OrderExecuted;

        public OrderEvaluator(Backtester backtester, BacktesterOrderManager orderManager)
        {
            _backtester = backtester;
            _orderManager = orderManager;
            _logger = backtester.Logger;
        }

        public void EvaluateOrder(string ticker, Order order, BidAsk bidAsk)
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
            if (o.Action == OrderAction.BUY)
            {
                if (o.StopPrice == double.MaxValue)
                {
                    o.StopPrice = o.TrailingPercent != double.MaxValue ?
                        bidAsk.Ask + o.TrailingPercent * bidAsk.Ask :
                        bidAsk.Ask + o.TrailingAmount;
                }

                if (o.StopPrice <= bidAsk.Ask)
                {
                    _logger?.Debug($"{o} : stop price of {o.StopPrice:c} reached. Ask : {bidAsk.Ask:c}");
                    ExecuteOrder(ticker, o, bidAsk.Ask);
                }
                else if (o.TrailingPercent != double.MaxValue)
                {
                    var currentPercent = (o.StopPrice - bidAsk.Ask) / bidAsk.Ask;
                    if (currentPercent > o.TrailingPercent)
                    {
                        var newVal = bidAsk.Ask + o.TrailingPercent * bidAsk.Ask;
                        _logger?.Trace($"{o} : current%={currentPercent} trail%={o.TrailingPercent} : adjusting stop price of {o.StopPrice:c} to {newVal:c}");
                        o.StopPrice = newVal;
                    }
                }
                else
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
                if (o.StopPrice == double.MaxValue)
                {
                    o.StopPrice = o.TrailingPercent != double.MaxValue ?
                        bidAsk.Bid - o.TrailingPercent * bidAsk.Bid :
                        bidAsk.Bid - o.TrailingAmount;
                }

                if (o.StopPrice >= bidAsk.Bid)
                {
                    _logger?.Debug($"{o} : stop price of {o.StopPrice:c} reached. Bid: {bidAsk.Bid:c}");
                    ExecuteOrder(ticker, o, bidAsk.Bid);
                }
                else if (o.TrailingPercent != double.MaxValue)
                {
                    var currentPercent = (o.StopPrice - bidAsk.Bid) / -bidAsk.Bid;
                    if (currentPercent > o.TrailingPercent)
                    {
                        var newVal = bidAsk.Bid - o.TrailingPercent * bidAsk.Bid;
                        _logger?.Trace($"{o} : current%={currentPercent} trail%={o.TrailingPercent} : adjusting stop price of {o.StopPrice:c} to {newVal:c}");
                        o.StopPrice = newVal;
                    }
                }
                else
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

            Position position = _backtester.Account.Positions[ticker];
            Account account = _backtester.Account;

            if (order.Action == OrderAction.BUY)
            {
                if (total > _backtester.Account.CashBalances["USD"])
                {
                    _logger?.Error($"{order} Cannot execute BUY order! Not enough funds (required : {total}, actual : {account.CashBalances["USD"]}");

                    // TODO : was this really necessary? Should be checked when order is placed?
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
                if (position.PositionAmount < order.TotalQuantity)
                {
                    _logger?.Error($"{order} Cannot execute SELL order! Not enough position (required : {order.TotalQuantity}, actual : {position.PositionAmount}");
                    // TODO : was this really necessary? Should be checked when order is placed?
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

            var oe = new OrderExecution()
            {
                AcctNumber = account.Code,
                OrderId = order.Id,
                Time = _backtester.CurrentTime,
                Action = order.Action,
                Shares = order.TotalQuantity,
                Price = total,
                AvgPrice = price,
                CommissionInfo = new CommissionInfo()
                {
                    Commission = commission,
                    Currency = "USD",
                    RealizedPNL = position.RealizedPNL
                }
            };

            OrderExecuted?.Invoke(ticker, oe);
        }
    }
}
