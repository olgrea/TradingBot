using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using TradingBot;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;
using TradingBot.Utils;

namespace HistoricalDataFetcher
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string ticker = args[0];
            DateTime startDate = DateTime.Parse(args[1]);


            // TWS API limitations. Pacing violation occurs when : 
            // - Making identical historical data requests within 15 seconds.
            // - Making six or more historical data requests for the same Contract, Exchange and Tick Type within two seconds.
            // - Making more than 60 requests within any ten minute period.
            // Step sizes for 5 secs bars : 3600 S

            var logger = new ConsoleLogger();
            var broker = new IBBroker(321, logger);
            broker.Connect();

            var contract = broker.GetContract(ticker);
            if (contract == null)
                throw new ArgumentException($"can't find contract for ticker {ticker}");


            int nbRequest = 0;
            DateTime morning = new DateTime(startDate.Year, startDate.Month, startDate.Day, 7, 0, 0, DateTimeKind.Local);
            DateTime current = DateTime.SpecifyKind(startDate.AddHours(16), DateTimeKind.Local);

            IEnumerable<Bar> dailyBars = new LinkedList<Bar>();

            while (current >= morning)
            {
                if(nbRequest == 60)
                {
                    logger.LogInfo($"60 requests made : waiting 10 minutes...");
                    for(int i=0; i<10;++i)
                    {
                        Task.Delay(60 * 1000);
                        if(i < 9)
                            logger.LogInfo($"{9-i} minutes left...");
                        else
                            logger.LogInfo($"Resuming historical data fetching");
                    }
                    nbRequest = 0;
                }
                else if (nbRequest % 5 == 0)
                {
                    logger.LogInfo($"{nbRequest} requests made : waiting 2 seconds...");
                    Task.Delay(2000);
                }

                var bars = broker.GetHistoricalDataAsync(contract, BarLength._5Sec, current.ToString(), 3600 / 5).Result;
                dailyBars = bars.Concat(dailyBars);

                current.AddHours(-1);
                nbRequest++;
            }
        }
    }
}