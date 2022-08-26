using System;
using TradingBot.Broker;
using TradingBot.Utils;

namespace TradingBot
{
    internal class Program
    {
        static void Main(string[] args)
        {

            var client = new IBBroker(new ConsoleLogger());
            client.Connect();



            //client.GetAccount();


            Console.ReadKey();
        }
    }

    public class ConsoleLogger : ILogger
    {
        public void LogDebug(string message) => Console.WriteLine(message);
        public void LogInfo(string message) => Console.WriteLine(message);
        public void LogError(string message) => Console.Error.WriteLine(message);
    }
}
