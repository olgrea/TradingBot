using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CommandLine;
using NLog;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;
using static TradingBot.Utils.MarketDataUtils;
using static TradingBot.Utils.MarketDataUtils.HistoricalDataFetcher;

[assembly: InternalsVisibleTo("Tests")]
namespace HistoricalDataFetcher
{
    internal class Program
    {
        public class Options
        {
            [Option('t', "ticker", Required = true, HelpText = "The symbol of the stock for which to retrieve historical data")]
            public string Ticker { get; set; } = "";

            [Option('s', "start", Required = true, HelpText = "The start date at which to retrieve historical data. Format : YYYY-MM-dd")]
            public string StartDate { get; set; } = "";

            [Option('e', "end", Required = false, HelpText = "When specified, historical data will be retrieved from start date to end date. Format : YYYY-MM-dd")]
            public string EndDate { get; set; } = "";
        }

        static async Task Main(string[] args)
        {
            var parsedArgs = Parser.Default.ParseArguments<Options>(args);

            string ticker = parsedArgs.Value.Ticker;
            DateTime startDate = DateTime.Parse(parsedArgs.Value.StartDate);
            DateTime endDate = startDate;
            if (!string.IsNullOrEmpty(parsedArgs.Value.EndDate))
                endDate = DateTime.Parse(parsedArgs.Value.EndDate);

            await FetchEverything(ticker, startDate.AddTicks(MarketStartTime.Ticks), endDate.AddTicks(MarketEndTime.Ticks));
        }

        public static async Task FetchEverything(string ticker, DateTime startDate, DateTime endDate)
        {
            var broker = new IBBroker(321);
            var logger = LogManager.GetLogger($"{nameof(TradingBot.Utils.MarketDataUtils.HistoricalDataFetcher)}");
            var dataFetcher = new TradingBot.Utils.MarketDataUtils.HistoricalDataFetcher(broker, logger);
            broker.ErrorHandler = new FetcherErrorHandler(dataFetcher, broker, logger); 

            await broker.ConnectAsync();
            var contract = await broker.GetContractAsync(ticker);
            if (contract == null)
                throw new ArgumentException($"can't find contract for ticker {ticker}");

            var marketDays = GetMarketDays(startDate, endDate).ToList();
            foreach ((DateTime, DateTime) pair in marketDays)
            {
                try
                {
                    await dataFetcher.GetDataForDay<Bar>(pair.Item1.Date, (pair.Item1.TimeOfDay, pair.Item2.TimeOfDay), contract);
                }
                catch (MarketHolidayException) { break; }
            }

            foreach ((DateTime, DateTime) pair in marketDays)
            {
                try
                {
                    await dataFetcher.GetDataForDay<BidAsk>(pair.Item1.Date, (pair.Item1.TimeOfDay, pair.Item2.TimeOfDay), contract);
                }
                catch (MarketHolidayException) { break; }
            }

            logger.Info($"\nComplete!\n");
        }
    }
}


