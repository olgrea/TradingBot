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

            [Option('m', "marketData",  Group ="marketData", Required = false, Default ="All", HelpText = "What type of data to fetch [All,Bar,BidAsk,Last]")]
            public string MarketData { get; set; } = "";
        }

        [Flags]
        public enum MarketDataOptions
        {
            None = 0,
            Bar = 1 << 0,
            BidAsk = 1 << 1,
            Last = 1 << 2,
            All = Bar | BidAsk | Last,
        }

        static async Task Main(string[] args)
        {
            var parsedArgs = Parser.Default.ParseArguments<Options>(args);

            string ticker = parsedArgs.Value.Ticker;
            DateTime startDate = DateTime.Parse(parsedArgs.Value.StartDate);
            DateTime endDate = startDate;
            if (!string.IsNullOrEmpty(parsedArgs.Value.EndDate))
                endDate = DateTime.Parse(parsedArgs.Value.EndDate);

            MarketDataOptions opts = MarketDataOptions.None;
            foreach(string s in parsedArgs.Value.MarketData.Split(','))
                opts |= Enum.Parse<MarketDataOptions>(s);

            if (opts == MarketDataOptions.None)
                throw new ArgumentException("No market data type specified", nameof(Options.MarketData));

            await FetchEverything(ticker, startDate.AddTicks(MarketDataUtils.MarketStartTime.Ticks), endDate.AddTicks(MarketDataUtils.MarketEndTime.Ticks), opts);
        }

        public static async Task FetchEverything(string ticker, DateTime startDate, DateTime endDate, MarketDataOptions marketDataOptions)
        {
            var client = new IBClient(321);
            var logger = LogManager.GetLogger($"{nameof(HistoricalDataFetcher)}");
            var dataFetcher = new HistoricalDataFetcher(client, logger);

            await client.ConnectAsync();
            var contract = await client.GetContractAsync(ticker);
            if (contract == null)
                throw new ArgumentException($"can't find contract for ticker {ticker}");

            var marketDays = MarketDataUtils.GetMarketDays(startDate, endDate).ToList();

            bool fetchEverything = marketDataOptions.HasFlag(MarketDataOptions.All);
            if(fetchEverything || marketDataOptions.HasFlag(MarketDataOptions.Bar))
            {
                var barCmdFactory = new BarCommandFactory(BarLength._1Sec);
                await Fetch(dataFetcher, contract, marketDays, barCmdFactory);
            }

            if (fetchEverything || marketDataOptions.HasFlag(MarketDataOptions.BidAsk))
            {
                var bidAskCmdFactory = new BidAskCommandFactory();
                await Fetch(dataFetcher, contract, marketDays, bidAskCmdFactory);
            }

            if (fetchEverything || marketDataOptions.HasFlag(MarketDataOptions.Last)) 
            {
                var lastCmdFactory = new LastCommandFactory();
                await Fetch(dataFetcher, contract, marketDays, lastCmdFactory);
            }

            // TODO : fetch and store BarLength._1Day

            logger.Info($"\nComplete!\n");
        }

        private static async Task Fetch<TMarketData>(HistoricalDataFetcher dataFetcher, Contract contract, List<(DateTime, DateTime)> marketDays, DbCommandFactory<TMarketData> cmdFactory) where TMarketData : IMarketData, new()
        {
            foreach ((DateTime, DateTime) pair in marketDays)
            {
                try
                {
                    await dataFetcher.GetDataForDay(pair.Item1.Date, (pair.Item1.TimeOfDay, pair.Item2.TimeOfDay), contract, cmdFactory);
                }
                catch (MarketHolidayException) { break; }
            }
        }
    }
}
