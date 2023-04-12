using CommandLine;
using NLog;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.IBKR;

namespace HistoricalDataGetterApp
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

            [Option('m', "marketData", Group = "marketData", Required = false, Default = "All", HelpText = "What type of data to fetch [All,Bar,BidAsk,Last]")]
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
            foreach (string s in parsedArgs.Value.MarketData.Split(','))
                opts |= Enum.Parse<MarketDataOptions>(s);

            if (opts == MarketDataOptions.None)
                throw new ArgumentException("No market data type specified", nameof(Options.MarketData));

            await FetchEverything(ticker, startDate.AddTicks(MarketDataUtils.MarketStartTime.Ticks), endDate.AddTicks(MarketDataUtils.MarketEndTime.Ticks), opts);
        }

        public static async Task FetchEverything(string ticker, DateTime startDate, DateTime endDate, MarketDataOptions marketDataOptions)
        {
            var logger = LogManager.GetLogger($"{nameof(HistoricalDataGetterApp)}");
            var broker = new IBBroker(800, logger);
            await broker.ConnectAsync();

            logger.Info($"Retrieving {marketDataOptions} for {ticker}, from {startDate.ToShortDateString()} to {endDate.ToShortDateString()}.");

            bool fetchEverything = marketDataOptions.HasFlag(MarketDataOptions.All);
            if (fetchEverything || marketDataOptions.HasFlag(MarketDataOptions.Bar))
            {
                await broker.HistoricalDataProvider.GetHistoricalOneSecBarsAsync(ticker, startDate, endDate);
            }

            if (fetchEverything || marketDataOptions.HasFlag(MarketDataOptions.BidAsk))
            {
                await broker.HistoricalDataProvider.GetHistoricalBidAsksAsync(ticker, startDate, endDate);
            }

            if (fetchEverything || marketDataOptions.HasFlag(MarketDataOptions.Last))
            {
                await broker.HistoricalDataProvider.GetHistoricalLastsAsync(ticker, startDate, endDate);
            }

            logger.Info($"\nComplete!\n");
        }
    }
}