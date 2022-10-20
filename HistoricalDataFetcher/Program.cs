using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CommandLine;
using NLog;
using TradingBot.Broker;
using TradingBot.Broker.Client;
using TradingBot.Broker.Client.Messages;
using TradingBot.Broker.MarketData;
using TradingBot.Utils;

[assembly: InternalsVisibleToAttribute("Tests")]
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

            var hdf = new DataFetcher(ticker, startDate, endDate);    
            await hdf.Start();
        }
    }

    internal class DataFetcher
    {
        public const string RootDir = MarketDataUtils.RootDir;
        public const string DbPath = "C:\\tradingbot\\db\\historicaldata.sqlite3";

        public int _nbRequest = 0;

        public int NbRequest
        {
            get => _nbRequest;
            set
            {
                _nbRequest = value;
                CheckForPacingViolations();
            }
        }

        public IBBroker _broker;
        public ILogger _logger;

        string _ticker;
        DateTime _startDate;
        DateTime _endDate;
        IErrorHandler _errorHandler;

        internal class FetcherErrorHandler : IBBrokerErrorHandler
        {
            DataFetcher _fetcher;
            public FetcherErrorHandler(DataFetcher fetcher, IBBroker broker, ILogger logger) : base(broker, logger)
            {
                _fetcher = fetcher;
            }

            public override bool IsHandled(ErrorMessageException msg)
            {
                switch (msg.ErrorCode)
                {
                    // TODO : handle pacing violation for when the program has been started and restarted a lot
                    default:
                        //_fetcher.Wait10Minutes();
                        return base.IsHandled(msg);
                }
            }
        }

        public DataFetcher(string ticker, DateTime start, DateTime end)
        {
            _ticker = ticker;

            var marketStartTime = MarketDataUtils.MarketStartTime;
            var marketEndTime = MarketDataUtils.MarketEndTime;
            _startDate = new DateTime(start.Year, start.Month, start.Day, 8, 0, 0);
            _endDate = new DateTime(end.Year, end.Month, end.Day, marketEndTime.Hours, marketEndTime.Minutes, marketEndTime.Seconds);

            _logger = LogManager.GetLogger($"{nameof(DataFetcher)}");
            _broker = new IBBroker(321);

            _errorHandler = new FetcherErrorHandler(this, _broker as IBBroker, _logger);
            _broker.ErrorHandler = _errorHandler;

        }

        public async Task Start()
        {
            await _broker.ConnectAsync();

            var contract = await _broker.GetContractAsync(_ticker);
            if (contract == null)
                throw new ArgumentException($"can't find contract for ticker {_ticker}");

            if(_startDate < _endDate)
            {
                var marketDays = MarketDataUtils.GetMarketDays(_startDate, _endDate).ToList();
                foreach ((DateTime, DateTime) pair in marketDays)
                    await GetDataForDay<Bar>(pair.Item1, contract);

                foreach ((DateTime, DateTime) pair in marketDays)
                    await GetDataForDay<BidAsk>(pair.Item1, contract);
            }
            else
            {
                await GetDataForDay<Bar>(_startDate, contract);
                await GetDataForDay<BidAsk>(_startDate, contract);
            }

            _logger.Info($"\nComplete!\n");
        }

        async Task GetDataForDay<TData>(DateTime date, Contract contract) where TData : IMarketData, new()
        {
            var marketStart = MarketDataUtils.MarketStartTime;
            var marketEnd = MarketDataUtils.MarketEndTime;

            DateTime morning = new DateTime(date.Year, date.Month, date.Day, marketStart.Hours, marketStart.Minutes, marketStart.Seconds, DateTimeKind.Local);
            DateTime current = new DateTime(date.Year, date.Month, date.Day, marketEnd.Hours, marketEnd.Minutes, marketEnd.Seconds, DateTimeKind.Local);

            _logger.Info($"Getting data for {contract.Symbol} on {date.ToShortDateString()} ({morning.ToShortTimeString()} to {current.ToShortTimeString()})");

            while (current >= morning)
            {
                var begin = current.AddMinutes(-30);
                var end = current;
                if (DataExists<TData>(contract.Symbol, current.Date, (begin.TimeOfDay, end.TimeOfDay)))
                {
                    var dateStr = current.Date.ToShortDateString();
                    _logger.Info($"Data for {contract.Symbol} {dateStr} ({begin.ToShortTimeString()}-{end.ToShortTimeString()}) already exists in db. Skipping.");
                }
                else
                {
                    try
                    {
                        LinkedList<TData> data = await FetchHistoricalData<TData>(contract, current);
                        SaveData(contract.Symbol, current, data);
                    }
                    catch (MarketHolidayException) { break; }
                }

                current = current.AddMinutes(-30);
            }
        }

        private void SaveData<TData>(string symbol, DateTime date, IEnumerable<TData> dailyData) where TData : IMarketData, new()
        {
            DbUtils.InsertData<TData>(symbol, dailyData);
        }

        private void CheckForPacingViolations()
        {
            // TWS API limitations. Pacing violation occurs when : 
            // - Making identical historical data requests within 15 seconds.
            // - Making six or more historical data requests for the same Contract, Exchange and Tick Type within two seconds.
            // - Making more than 60 requests within any ten minute period.
            // https://interactivebrokers.github.io/tws-api/historical_limitations.html

            if (_nbRequest == 60)
            {
                _logger.Info($"60 requests made : waiting 10 minutes...");
                Wait10Minutes();
                _nbRequest = 0;
            }
            else if (_nbRequest != 0 && _nbRequest % 5 == 0)
            {
                _logger.Info($"{NbRequest} requests made : waiting 2 seconds...");
                Task.Delay(2000).Wait();
            }
        }

        void Wait10Minutes()
        {
            for (int i = 0; i < 10; ++i)
            {
                Task.Delay(60 * 1000).Wait();
                if (i < 9)
                    _logger.Info($"{9 - i} minutes left...");
                else
                    _logger.Info($"Resuming historical data fetching");
            }
        }

        class MarketHolidayException : Exception { }

        async Task<LinkedList<TData>> FetchHistoricalData<TData>(Contract contract, DateTime time) where TData : IMarketData, new()
        {
            LinkedList<TData> data = await Fetch<TData>(contract, time);
            if (IsPossibleMarketHoliday(time, data))
            {
                _logger.Info($"Possible market holiday on {time} (returned data time mismatch). Skipping.");
                throw new MarketHolidayException();
            }

            return data;
        }

        private bool DataExists<TData>(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange) where TData : IMarketData, new()
        {
            return DbUtils.DataExistsInDb<TData>(symbol, date, timeRange);
        }

        bool IsPossibleMarketHoliday<TData>(DateTime time, IEnumerable<TData> data) where TData : IMarketData, new()
        {
            // TODO : better way to know if the market was opened or not?
            // On market holidays, TWS seems to return the bars of the previous trading day.
            var d = data.FirstOrDefault();
            return d != null && time.Date != d.Time.Date;
        }

        async Task<LinkedList<TData>> Fetch<TData>(Contract contract, DateTime time) where TData : IMarketData, new()
        {
            if (typeof(TData) == typeof(Bar))
            {
                return await FetchBars<TData>(contract, time);
            }
            else if (typeof(TData) == typeof(BidAsk))
            {
                return await FetchBidAsk<TData>(contract, time);
            }

            return new LinkedList<TData>();
        }

        private async Task<LinkedList<TData>> FetchBidAsk<TData>(Contract contract, DateTime time) where TData : IMarketData, new()
        {
            _logger.Info($"Retrieving bid ask from TWS for '{contract.Symbol} {time}'.");

            // max nb of ticks per request is 1000 so we need to do multiple requests for 30 minutes...
            // There doesn't seem to be a way to convert ticks to seconds... 1 tick != 1 seconds apparently. 
            // So we just do requests as long as we don't have 30 minutes.
            IEnumerable<BidAsk> bidask = new LinkedList<BidAsk>();
            DateTime current = time;
            TimeSpan _30min = TimeSpan.FromMinutes(30);
            //TimeSpan _20min = TimeSpan.FromMinutes(20);
            var diff = time - current;
            int tickCount = 1000;
            while (diff <= _30min)
            {
                //// Adjusting tick count for the last 5 minutes in order to not retrieve too much out of range data...
                //if(diff > _20min)
                //    tickCount = 100;

                var ticks = await _broker.RequestHistoricalTicks(contract, current, tickCount);

                // Note that when BID_ASK historical data is requested, each request is counted twice according to the doc
                NbRequest++; NbRequest++;
                if (IsPossibleMarketHoliday(current, ticks))
                    return new LinkedList<TData>();

                bidask = ticks.Concat(bidask);
                current = ticks.First().Time;

                diff = time - current;
            }

            // Remove out of range data.
            var list = new LinkedList<TData>(bidask.Cast<TData>());
            var currNode = list.First;
            var timeOfDay = (time - _30min).TimeOfDay;
            while (currNode != null && currNode.Value.Time.TimeOfDay < timeOfDay)
            {
                list.RemoveFirst();
                currNode = list.First;
            }

            return list;
        }

        private async Task<LinkedList<TData>> FetchBars<TData>(Contract contract, DateTime time) where TData : IMarketData, new()
        {
            _logger.Info($"Retrieving bars from TWS for '{contract.Symbol} {time}'.");
            var bars = await _broker.GetHistoricalDataAsync(contract, BarLength._1Sec, time, 1800);
            NbRequest++;
            return new LinkedList<TData>(bars.Cast<TData>());
        }
    }
}


