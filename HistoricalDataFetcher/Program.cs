using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;
using TradingBot.Utils;
using System.IO;

namespace HistoricalDataFetcher
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string ticker = args[0];
            DateTime startDate = DateTime.Parse(args[1]);
            DateTime endDate = startDate;
            if (args.Length == 3)
                endDate = DateTime.Parse(args[2]);

            var hdf = new HistoricalDataFetcher(ticker, startDate, endDate);    
            hdf.Start();
        }
    }

    internal class HistoricalDataFetcher
    {
        public const string RootDir = "historical";

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
        public ConsoleLogger _logger;

        string _ticker;
        DateTime _startDate;
        DateTime _endDate;

        public static TimeSpan MarketStart = DateTimeUtils.MarketStartTime;
        public static TimeSpan MarketEnd = DateTimeUtils.MarketEndTime;

        public HistoricalDataFetcher(string ticker, DateTime start, DateTime end)
        {
            _ticker = ticker;

            var marketStartTime = DateTimeUtils.MarketStartTime;
            var marketEndTime = DateTimeUtils.MarketEndTime;
            _startDate = new DateTime(start.Year, start.Month, start.Day, marketStartTime.Hours, marketStartTime.Minutes, marketStartTime.Seconds);
            _endDate = new DateTime(end.Year, end.Month, end.Day, marketEndTime.Hours, marketEndTime.Minutes, marketEndTime.Seconds);

            _logger = new ConsoleLogger();
            _broker = new IBBroker(321, new NoLogger());
        }

        public void Start()
        {
            _broker.Connect();

            var contract = _broker.GetContract(_ticker);
            if (contract == null)
                throw new ArgumentException($"can't find contract for ticker {_ticker}");

            string tickerDir = Path.Combine(RootDir, _ticker);
            if (!Directory.Exists(tickerDir))
                Directory.CreateDirectory(tickerDir);

            if(_startDate < _endDate)
            {
                foreach((DateTime, DateTime) pair in DateTimeUtils.GetMarketDays(_startDate, _endDate))
                {
                    GetDataForDay<Bar>(pair.Item1, contract);
                    GetDataForDay<BidAsk>(pair.Item1, contract);
                }
            }
            else
            {
                GetDataForDay<Bar>(_startDate, contract);
                GetDataForDay<BidAsk>(_startDate, contract);
            }

            _logger.LogInfo($"\nComplete!\n");
        }

        void GetDataForDay<TData>(DateTime date, Contract contract) where TData : IMarketData, new()
        {
            var marketStart = DateTimeUtils.MarketStartTime;
            var marketEnd = DateTimeUtils.MarketEndTime;

            DateTime morning = new DateTime(date.Year, date.Month, date.Day, marketStart.Hours, marketStart.Minutes, marketStart.Seconds, DateTimeKind.Local);
            DateTime current = new DateTime(date.Year, date.Month, date.Day, marketEnd.Hours, marketEnd.Minutes, marketEnd.Seconds, DateTimeKind.Local);
            
            IEnumerable<TData> dailyData = new LinkedList<TData>();
            _logger.LogInfo($"Getting data for {contract.Symbol} on {date.ToString("yyyy-MM-dd")} ({morning.ToShortTimeString()} to {current.ToShortTimeString()})");

            while (current >= morning)
            {
                CheckForPacingViolations();

                LinkedList<TData> data = FetchHistoricalData<TData>(contract, current);

                // TODO : better way to know if the market was opened?
                // On market holidays, TWS seems to return the bars of the previous trading day.
                var d = data.FirstOrDefault();
                if (d != null && current.Date != d.Time.Date)
                {
                    _logger.LogInfo($"Possible market holiday on {date} (returned data time mismatch). Skipping.");
                    break;
                }

                current = current.AddHours(-1);
                dailyData = data.Concat(dailyData);
            }

            string filename = Path.Combine(RootDir, MarketDataUtils.MakeDailyDataPath<TData>(_ticker, date));
            MarketDataUtils.SerializeData(filename, dailyData);
        }

        private void CheckForPacingViolations()
        {
            // TWS API limitations. Pacing violation occurs when : 
            // - Making identical historical data requests within 15 seconds.
            // - Making six or more historical data requests for the same Contract, Exchange and Tick Type within two seconds.
            // - Making more than 60 requests within any ten minute period.
            // https://interactivebrokers.github.io/tws-api/historical_limitations.html

            if (NbRequest == 60)
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
                NbRequest = 0;
            }
            else if (NbRequest != 0 && NbRequest % 5 == 0)
            {
                _logger.LogInfo($"{NbRequest} requests made : waiting 2 seconds...");
                Task.Delay(2000).Wait();
            }
        }

        LinkedList<TData> FetchHistoricalData<TData>(Contract contract, DateTime current) where TData : IMarketData, new()
        {
            string filename = Path.Combine(RootDir, MarketDataUtils.MakeHourlyDataPath<TData>(contract.Symbol, current));
            LinkedList<TData> data;
            if (File.Exists(filename))
            {
                _logger.LogInfo($"File '{filename}' exists. Restoring from dicks.");
                data = new LinkedList<TData>(MarketDataUtils.DeserializeData<TData>(filename));
            }
            else
            {
                data = Fetch<TData>(filename, contract, current);

                var d = data.FirstOrDefault();
                if (d != null && current.Date == d.Time.Date)
                    MarketDataUtils.SerializeData(filename, data);
            }

            return data;
        }

        LinkedList<TData> Fetch<TData>(string filename, Contract contract, DateTime current) where TData : IMarketData, new()
        {
            if (typeof(TData) == typeof(Bar))
            {
                _logger.LogInfo($"Retrieving bars from TWS for '{filename}'.");
                var bars = _broker.GetHistoricalDataAsync(contract, BarLength._1Sec, current, 1800).Result;
                
                NbRequest++;
                return new LinkedList<TData>(bars.Cast<TData>());
            }
            else if (typeof(TData) == typeof(BidAsk))
            {

                _logger.LogInfo($"Retrieving bid ask from TWS for '{filename}'.");
                //var bidask = _broker.GetPastBidAsks(contract, current);

                // Note that when BID_ASK historical data is requested, each request is counted twice
                NbRequest++;
                NbRequest++;

                return new LinkedList<TData>(bidask.Cast<TData>());
            }

            return new LinkedList<TData>();
        }
    }
}


