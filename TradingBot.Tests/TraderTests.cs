using System;
using System.Collections.Generic;
using System.Text;
using InteractiveBrokers.Messages;
using InteractiveBrokers.Orders;
using NUnit.Framework;
using System.Threading.Tasks;
using MarketDataUtils = InteractiveBrokers.MarketData.Utils;

namespace TradingBot.Tests
{
    internal class TraderTests
    {
        // TODO : enforce disallowing of stock shorting in Trader
        //[Test]
        //public async Task PlaceSellOrder_WhenNotEnoughPosition_ShouldFail()
        //{
        //    if (!MarketDataUtils.IsMarketOpen())
        //        Assert.Ignore();

        //    // Setup
        //    var trader = new Trader("GME");
        //    var buyOrder = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = 5 };
        //    var buyOrderResult = trader.PlaceOrderAsync(buyOrder);
        //    Assert.IsTrue(buyOrderResult?.OrderState.Status == Status.PreSubmitted || buyOrderResult?.OrderState.Status == Status.Submitted);

        //    // Test
        //    var sellOrder = new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = 10 };
        //    Exception ex = null;
        //    OrderResult sellOrderResult = null;
        //    try
        //    {
        //        sellOrderResult = trader.PlaceOrderAsync(sellOrder);
        //    }
        //    catch (Exception e)
        //    {
        //        ex = e;
        //    }
        //    finally
        //    {
        //        var r = sellOrderResult as OrderPlacedResult;
        //        Assert.IsNull(r);
        //        Assert.IsInstanceOf<ErrorMessageException>(ex);
        //    }
        //}
    }
}
