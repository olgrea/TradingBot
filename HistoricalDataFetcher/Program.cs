using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using TradingBot;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;
using TradingBot.Utils;
using System.IO;

namespace HistoricalDataFetcher
{
    internal class Program
    {
        public const string root = "historical";

        static void Main(string[] args)
        {
            string ticker = args[0];
            DateTime startDate = DateTime.Parse(args[1]);

            
            if (!Directory.Exists(root))
                Directory.CreateDirectory(root);

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
            DateTime current = new DateTime(startDate.Year, startDate.Month, startDate.Day, 16, 0, 0, DateTimeKind.Local);

            string tmpDir = Path.Combine(root, $"{startDate.ToString("yyyy-MM-dd")}");
            IEnumerable<Bar> dailyBars = new LinkedList<Bar>();

            while (current >= morning)
            {
                if (nbRequest == 60)
                {
                    logger.LogInfo($"60 requests made : waiting 10 minutes...");
                    for (int i = 0; i < 10; ++i)
                    {
                        Task.Delay(60 * 1000);
                        if (i < 9)
                            logger.LogInfo($"{9 - i} minutes left...");
                        else
                            logger.LogInfo($"Resuming historical data fetching");
                    }
                    nbRequest = 0;
                }
                else if (nbRequest != 0 && nbRequest % 5 == 0)
                {
                    logger.LogInfo($"{nbRequest} requests made : waiting 2 seconds...");
                    Task.Delay(2000);
                }

                LinkedList<Bar> bars = GetHistoricalData(broker, contract, current, tmpDir);
                dailyBars = bars.Concat(dailyBars);

                current = current.AddHours(-1);
                nbRequest++;
            }
        }

        private static LinkedList<Bar> GetHistoricalData(IBBroker broker, Contract contract, DateTime current, string tmpDir)
        {
            if (!Directory.Exists(tmpDir))
                Directory.CreateDirectory(tmpDir);

            string filename = Path.Combine(tmpDir, $"{current.ToString("yyyy-MM-dd HH-mm-ss")}.json");
            LinkedList<Bar> bars;
            if (File.Exists(filename))
            {
                bars = Serialization.DeserializeBars(filename);
            }
            else
            {
                bars = broker.GetHistoricalDataAsync(contract, BarLength._5Sec, current.ToUniversalTime().ToString("yyyyMMdd-HH:mm:ss"), 3600 / 5).Result;
            }

            if (!File.Exists(filename))
            {
                Serialization.SerializeBars(filename, bars);
            }

            return bars;
        }
    }
}