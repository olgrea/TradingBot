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
            //_broker = new IBBroker(321, _logger);
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
                var marketDays = DateTimeUtils.GetMarketDays(_startDate, _endDate).ToList();
                foreach ((DateTime, DateTime) pair in marketDays)
                    GetDataForDay<Bar>(pair.Item1, contract);

                foreach ((DateTime, DateTime) pair in marketDays)
                    GetDataForDay<BidAsk>(pair.Item1, contract);
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
                LinkedList<TData> data = FetchHistoricalData<TData>(contract, current);
                if (data == null || data.Count == 0)
                    break;

                current = current.AddMinutes(-30);
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
                _logger.LogInfo($"{NbRequest} requests made : waiting 2 seconds...");
                Task.Delay(2000).Wait();
            }
        }

        LinkedList<TData> FetchHistoricalData<TData>(Contract contract, DateTime time) where TData : IMarketData, new()
        {
            string filename = Path.Combine(RootDir, MarketDataUtils.MakeDataPath<TData>(contract.Symbol, time));
            LinkedList<TData> data;
            if (File.Exists(filename))
            {
                _logger.LogInfo($"File '{filename}' exists. Restoring from disk.");
                data = new LinkedList<TData>(MarketDataUtils.DeserializeData<TData>(filename));
            }
            else
            {
                data = Fetch<TData>(filename, contract, time);
                if (IsPossibleMarketHoliday(time, data))
                {
                    _logger.LogInfo($"Possible market holiday on {time} (returned data time mismatch). Skipping.");
                    return new LinkedList<TData>();
                }

                MarketDataUtils.SerializeData(filename, data);
            }

            return data;
        }

        bool IsPossibleMarketHoliday<TData>(DateTime time, IEnumerable<TData> data) where TData : IMarketData, new()
        {
            // TODO : better way to know if the market was opened or not?
            // On market holidays, TWS seems to return the bars of the previous trading day.
            var d = data.FirstOrDefault();
            return d != null && time.Date != d.Time.Date;
        }

        LinkedList<TData> Fetch<TData>(string filename, Contract contract, DateTime time) where TData : IMarketData, new()
        {
            if (typeof(TData) == typeof(Bar))
            {
                return FetchBars<TData>(filename, contract, time);
            }
            else if (typeof(TData) == typeof(BidAsk))
            {
                return FetchBidAsk<TData>(filename, contract, time);
            }

            return new LinkedList<TData>();
        }

        private LinkedList<TData> FetchBidAsk<TData>(string filename, Contract contract, DateTime time) where TData : IMarketData, new()
        {
            _logger.LogInfo($"Retrieving bid ask from TWS for '{filename}'.");

            // max nb of ticks per request is 1000 so we need to do multiple requests for 30 minutes...
            // There doesn't seem to be a way to convert ticks to seconds... 1 tick != 1 seconds apparently. 
            // So we just do requests as long as we don't have 30 minutes.
            IEnumerable<BidAsk> bidask = new LinkedList<BidAsk>();
            DateTime current = time;
            TimeSpan _30min = TimeSpan.FromMinutes(30);
            TimeSpan _20min = TimeSpan.FromMinutes(20);
            var diff = time - current;
            int tickCount = 1000;
            while (diff <= _30min)
            {
                // Adjusting tick count for the last 5 minutes in order to not retrieve too much out of range data...
                if(diff > _20min)
                    tickCount = 100;

                var ticks = _broker.RequestHistoricalTicks(contract, current, tickCount).Result;

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

        private LinkedList<TData> FetchBars<TData>(string filename, Contract contract, DateTime time) where TData : IMarketData, new()
        {
            _logger.LogInfo($"Retrieving bars from TWS for '{filename}'.");
            var bars = _broker.GetHistoricalDataAsync(contract, BarLength._1Sec, time, 1800).Result;
            NbRequest++;
            return new LinkedList<TData>(bars.Cast<TData>());
        }
    }
}


