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
            string ticker = args[0];
            DateTime startDate = DateTime.Parse(args[1]);
            DateTime endDate = startDate;
            if (args.Length == 3)
                endDate = DateTime.Parse(args[2]);

            var bt = new Backtester(ticker, startDate, endDate, new ConsoleLogger());
            bt.Start();
        }
    }
}
