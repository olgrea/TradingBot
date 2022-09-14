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
        public static int _nbRequest = 0;
        public static IBBroker _broker;
        public static ConsoleLogger _logger;

        public static TimeSpan MarketStart = DateTimeUtils.MarketStartTime;
        public static TimeSpan MarketEnd = DateTimeUtils.MarketEndTime;

        static void Main(string[] args)
        {
            string ticker = args[0];
            DateTime startDate = DateTime.Parse(args[1]);
            DateTime endDate = startDate;
            if (args.Length == 3)
                endDate = DateTime.Parse(args[2]);

            _logger = new ConsoleLogger();
            _broker = new IBBroker(321, new NoLogger());
            _broker.Connect();

            var contract = _broker.GetContract(ticker);
            if (contract == null)
                throw new ArgumentException($"can't find contract for ticker {ticker}");

            string tickerDir = Path.Combine(root, ticker);
            if (!Directory.Exists(tickerDir))
                Directory.CreateDirectory(tickerDir);
            

            if(startDate < endDate)
            {
                foreach((DateTime, DateTime) pair in DateTimeUtils.GetMarketDays(startDate, endDate))
                {
                    GetDataForDay(pair.Item1, contract, tickerDir);
                }
            }
            else
            {
                GetDataForDay(startDate, contract, tickerDir);
            }

            _logger.LogInfo($"\nComplete!\n");
        }

        private static void GetDataForDay(DateTime date, Contract contract, string tickerDir)
        {
            var marketStart = DateTimeUtils.MarketStartTime;
            var marketEnd = DateTimeUtils.MarketEndTime;

            DateTime morning = new DateTime(date.Year, date.Month, date.Day, marketStart.Hours, marketStart.Minutes, marketStart.Seconds, DateTimeKind.Local);
            DateTime current = new DateTime(date.Year, date.Month, date.Day, marketEnd.Hours, marketEnd.Minutes, marketEnd.Seconds, DateTimeKind.Local);

            string outDir = Path.Combine(tickerDir, $"{date.ToString("yyyy-MM-dd")}");
            IEnumerable<Bar> dailyBars = new LinkedList<Bar>();

            _logger.LogInfo($"Getting data for {contract.Symbol} on {date.ToString("yyyy-MM-dd")} ({morning.ToShortTimeString()} to {current.ToShortTimeString()})");

            // TWS API limitations. Pacing violation occurs when : 
            // - Making identical historical data requests within 15 seconds.
            // - Making six or more historical data requests for the same Contract, Exchange and Tick Type within two seconds.
            // - Making more than 60 requests within any ten minute period.
            // Step sizes for 5 secs bars : 3600 S

            while (current >= morning)
            {
                if (_nbRequest == 60)
                {
                    _logger.LogInfo($"60 requests made : waiting 10 minutes...");
                    for (int i = 0; i < 10; ++i)
                    {
                        Task.Delay(60 * 1000).Wait();
                        if (i < 9)
                            _logger.LogInfo($"{9 - i} minutes left...");
                        else
                            _logger.LogInfo($"Resuming historical data fetching");
                    }
                    _nbRequest = 0;
                }
                else if (_nbRequest != 0 && _nbRequest % 5 == 0)
                {
                    _logger.LogInfo($"{_nbRequest} requests made : waiting 2 seconds...");
                    Task.Delay(2000).Wait(); 
                }

                LinkedList<Bar> bars = FetchHistoricalData(contract, current, outDir);
                dailyBars = bars.Concat(dailyBars);

                current = current.AddHours(-1);
            }

            string filename = Path.Combine(outDir, $"full.json");
            if (!File.Exists(filename))
            {
                Serialization.SerializeBars(filename, dailyBars);
            }
        }

        private static LinkedList<Bar> FetchHistoricalData(Contract contract, DateTime current, string outDir)
        {
            if (!Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            string filename = Path.Combine(outDir, $"{current.ToString("yyyy-MM-dd HH-mm-ss")}.json");
            LinkedList<Bar> bars;
            if (File.Exists(filename))
            {
                _logger.LogInfo($"File '{filename}' exists. Restoring from dicks.");
                bars = new LinkedList<Bar>(Serialization.DeserializeBars(filename));
            }
            else
            {
                _logger.LogInfo($"Retrieving bars from TWS for '{filename}'.");
                bars = _broker.GetHistoricalDataAsync(contract, BarLength._5Sec, $"{current.ToString("yyyyMMdd HH:mm:ss")} US/Eastern", 3600 / 5).Result;
                _nbRequest++;
            }

            if (!File.Exists(filename))
            {
                Serialization.SerializeBars(filename, bars);
            }

            return bars;
        }
    }
}