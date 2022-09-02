using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Utils;
using Bar = TradingBot.Broker.MarketData.Bar;

namespace TradingBot
{
    internal class Program
    {
        static void Main(string[] args)
        {

            var client = new IBBroker(1337, new ConsoleLogger());
            client.Connect();

            var account = client.GetAccount();

            var contract = client.GetContract("GME");

            //client.RequestBars(contract, BarLength._30Sec, OnBarReceived);
            //client.RequestBidAsk(contract, OnBidAskReceived);

            var o1 = new LimitOrder()
            {
                Action = OrderAction.BUY,
                TotalQuantity = 200,
                LmtPrice = 15,
            };

            var o2 = new MarketIfTouchedOrder()
            {
                Action = OrderAction.SELL,
                TotalQuantity = 50,
                TouchPrice = 100
            };

            var o3 = new StopOrder()
            {
                Action = OrderAction.SELL,
                TotalQuantity = 25,
                StopPrice = 95
            };

            var o4 = new StopOrder()
            {
                Action = OrderAction.SELL,
                TotalQuantity = 100,
                StopPrice = 10
            };

            o1.RequestInfo.Transmit = o2.RequestInfo.Transmit = o3.RequestInfo.Transmit = o4.RequestInfo.Transmit = true;

            var o2chain = new OrderChain(o2, new List<OrderChain>() { o3 });
            var o1chain = new OrderChain(o1, new List<OrderChain>() { o2chain, o4 });

            //client.PlaceOrder(contract, o1chain);

            Console.ReadKey();
            Console.ReadKey();
            client.Disconnect();
        }

        static void OnBidAskReceived(Broker.Contract contract, BidAsk bidAsk)
        {
            Console.WriteLine($"{contract.Symbol} : time {bidAsk.Time} bid {bidAsk.Bid} bid size {bidAsk.BidSize} ask {bidAsk.Ask} ask size {bidAsk.AskSize}");
        }

        static void OnBarReceived(Broker.Contract contract, Bar bar)
        {
            Console.WriteLine($"{contract.Symbol} : time {bar.Time} open {bar.Open} high {bar.High} low {bar.Low} close {bar.Close} volume {bar.Volume} count {bar.TradeAmount}");
        }
    }


    public class ConsoleLogger : ILogger
    {
        public void LogDebug(string message) => Console.WriteLine(message);
        public void LogInfo(string message) => Console.WriteLine(message);
        public void LogWarning(string message) => Console.Error.WriteLine(message);
        public void LogError(string message) => Console.Error.WriteLine(message);
    }
}
