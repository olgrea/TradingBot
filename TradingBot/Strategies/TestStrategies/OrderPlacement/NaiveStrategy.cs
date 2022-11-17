using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using InteractiveBrokers.Orders;

namespace TradingBot.Strategies.TestStrategies
{
    internal class NaiveStrategy : IOrderStrategy
    {
        public NaiveStrategy(Trader trader)
        {
            Trader = trader;
        }
        public Trader Trader { get; }

        public async Task ManageOrders(IEnumerable<TradeSignal> signals)
        {
            var signal = signals.First();
            if (signal == TradeSignal.Neutral)
                return;

            var latestBidAsk = await Trader.Client.GetLatestBidAskAsync(Trader.Contract);
            if (signal >= TradeSignal.Buy)
            {
                int qty = (int)Math.Round(Trader.Account.AvailableBuyingPower / latestBidAsk.Ask);
                if (qty <= 0)
                    return;

                var buyOrder = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = qty };
                await Trader.OrderManager.PlaceOrderAsync(Trader.Contract, buyOrder);

            }
            else if (signal <= TradeSignal.Sell)
            {
                int qtyToSell = (int)Trader.Position.PositionAmount;
                if (qtyToSell <= 0)
                    return;

                var sellOrder = new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = qtyToSell };
                await Trader.OrderManager.PlaceOrderAsync(Trader.Contract, sellOrder);
            }
        }
    }
}
