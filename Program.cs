using System;
using System.Threading.Tasks;
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

            var account = client.GetAccount();

            var contract = client.GetContract("GME");

            Console.ReadKey();
            client.Disconnect();
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
