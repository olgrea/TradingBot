using System;
using System.Globalization;
using TradingBot.Utils;

namespace Backtester
{
    internal class Program
    {
        // DateTime.ParseExact(bar.Time, "yyyyMMdd  HH:mm:ss", CultureInfo.InvariantCulture)

        static void Main(string[] args)
        {
            DateTime start = new DateTime(2022, 9, 1, 15, 5, 5);
            DateTime end = new DateTime(2022, 9, 9, 10, 5, 5);
            var bt = new Backtester("GME", start, end, new ConsoleLogger());

            bt.Start();

            Console.ReadKey();
        }
    }
}
