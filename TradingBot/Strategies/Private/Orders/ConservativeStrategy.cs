using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InteractiveBrokers.Orders;

namespace TradingBot.Strategies
{
    internal class ConservativeStrategy : IOrderStrategy
    {
        public ConservativeStrategy(Trader trader)
        {
            Trader = trader;
        }

        Trader Trader { get; set; }

        public async Task ManageOrders(IEnumerable<TradeSignal> signals)
        {
            // TODO : handle multiple signals
            var signal = signals.First();
            if (signal == TradeSignal.Neutral)
                return;

            //TODO : refine this. Naive implementation first.
            var latestBidAsk = await Trader.Client.GetLatestBidAskAsync(Trader.Contract);
            if (signal >= TradeSignal.Buy)
            {
                if (Trader.Position.InAny())
                {
                    // Average down?
                    //if(_PnL.UnrealizedPnL < 0)
                    //{
                    //}
                    return;
                }
                else
                {
                    double cashToInvest = 0;
                    double acceptableRisk = 0;
                    if (signal == TradeSignal.CautiousBuy)
                    {
                        cashToInvest = Trader.Account.AvailableBuyingPower / 4.0;
                        acceptableRisk = 0.05;
                    }
                    else if (signal == TradeSignal.Buy)
                    {
                        cashToInvest = Trader.Account.AvailableBuyingPower / 2.0;
                        acceptableRisk = 0.10;
                    }
                    else if (signal == TradeSignal.StrongBuy)
                    {
                        cashToInvest = Trader.Account.AvailableBuyingPower;
                        acceptableRisk = 0.15;
                    }

                    if (cashToInvest == 0)
                        return;

                    int qty = (int)Math.Round(cashToInvest / latestBidAsk.Ask);

                    // TODO : should use fill price;
                    double stopPrice = latestBidAsk.Ask * (1 - acceptableRisk);

                    // TODO : investigate IBKR adaptive "split spread" orders
                    var buyOrder = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = qty };
                    var stopOrder = new StopOrder() { Action = OrderAction.SELL, TotalQuantity = qty, StopPrice = stopPrice };
                    var chain = new OrderChain(buyOrder, stopOrder);
                    await Trader.OrderManager.PlaceOrderAsync(Trader.Contract, chain);
                }

            }
            else if (signal <= TradeSignal.Sell)
            {
                if (!Trader.Position.InAny())
                    return;

                int qtyToSell = 0;
                if (signal == TradeSignal.CautiousSell)
                {
                    // TODO : check other metrics
                    return;
                }
                if (signal == TradeSignal.Sell)
                {
                    qtyToSell = (int)Trader.Position.PositionAmount / 2;
                }
                else if (signal == TradeSignal.StrongSell)
                {
                    qtyToSell = (int)Trader.Position.PositionAmount;
                }

                if (qtyToSell > 0)
                {
                    var sellOrder = new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = qtyToSell };
                    await Trader.OrderManager.PlaceOrderAsync(Trader.Contract, sellOrder);

                    // TODO : cancel hard stop order
                    //_orderManager.CancelOrder(stopOrder);
                }
            }
        }
    }
}
