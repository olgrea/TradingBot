using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;
using TradingBot.Utils;
using Bar = TradingBot.Broker.MarketData.Bar;

namespace TradingBot
{
    internal class Program
    {
        static void Main(string[] args)
        {

            var client = new IBBroker(new ConsoleLogger());
            client.Connect();

            //var account = client.GetAccount();

            var contract = client.GetContract("GME");

            //client.RequestBars(contract, BarLength._30Sec, OnBarReceived);
            //client.RequestBidAsk(contract, OnBidAskReceived);

            var o1 = new MarketOrder()
            {
                Action = OrderAction.BUY,
                TotalQuantity = 100,
            };

            var o2 = new MarketOrder()
            {
                Action = OrderAction.SELL,
                TotalQuantity = 125,
            };

            var o3 = new MarketOrder()
            {
                Action = OrderAction.BUY,
                TotalQuantity = 25,
                Transmit = false;
            };

            var l = new List<Order>() { o1, o2, o3 };

            //client.PlaceOrder(contract, order);
            client.PlaceOrders(contract, l);

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
