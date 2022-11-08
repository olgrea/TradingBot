using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CommandLine;
using DataStorage.Db.DbCommandFactories;
using InteractiveBrokers;
using InteractiveBrokers.Contracts;
using InteractiveBrokers.MarketData;
using NLog;
using static HistoricalDataFetcherApp.HistoricalDataFetcher;

[assembly: InternalsVisibleTo("Tests")]
namespace HistoricalDataFetcherApp
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

            await FetchEverything(ticker, startDate.AddTicks(Utils.MarketStartTime.Ticks), endDate.AddTicks(Utils.MarketEndTime.Ticks));
        }

        public static async Task FetchEverything(string ticker, DateTime startDate, DateTime endDate)
        {
            var broker = new IBClient(321);
            var logger = LogManager.GetLogger($"{nameof(HistoricalDataFetcher)}");
            var dataFetcher = new HistoricalDataFetcher(broker, logger);
            broker.ErrorHandler = new HistoricalDataFetcher.FetcherErrorHandler(dataFetcher, broker, logger);

            await broker.ConnectAsync();
            var contract = await broker.GetContractAsync(ticker);
            if (contract == null)
                throw new ArgumentException($"can't find contract for ticker {ticker}");

            var marketDays = Utils.GetMarketDays(startDate, endDate).ToList();

            var barCmdFactory = new BarCommandFactory(BarLength._1Sec);
            await Fetch(dataFetcher, contract, marketDays, barCmdFactory);

            var bidAskCmdFactory = new BidAskCommandFactory();
            await Fetch(dataFetcher, contract, marketDays, bidAskCmdFactory);

            var lastCmdFactory = new LastCommandFactory();
            await Fetch(dataFetcher, contract, marketDays, lastCmdFactory);

            // TODO : fetch and store BarLength._1Day

            logger.Info($"\nComplete!\n");
        }

        private static async Task Fetch<TMarketData>(HistoricalDataFetcher dataFetcher, Contract contract, List<(DateTime, DateTime)> marketDays, DbCommandFactory<TMarketData> cmdFactory) where TMarketData : IMarketData, new()
        {
            foreach ((DateTime, DateTime) pair in marketDays)
            {
                try
                {
                    await dataFetcher.GetDataForDay<TMarketData>(pair.Item1.Date, (pair.Item1.TimeOfDay, pair.Item2.TimeOfDay), contract, cmdFactory);
                }
                catch (MarketHolidayException) { break; }
            }
        }
    }
}
