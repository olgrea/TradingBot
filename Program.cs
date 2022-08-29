using System;
using System.Threading.Tasks;
using IBApi;
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

            client.RequestBars(contract, BarLength._30Sec, OnBarReceived);
            //client.RequestBidAsk(contract, OnBidAskReceived);

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
