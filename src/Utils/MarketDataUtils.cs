using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NLog;
using System.Threading.Tasks;
using TradingBot.Broker.Client.Messages;
using TradingBot.Broker.Client;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;
using TradingBot.Utils.Db;
using TradingBot.Utils.Db.DbCommandFactories;

namespace TradingBot.Utils
{
    public static class MarketDataUtils
    {
        public const string RootDir = @"C:\tradingbot\oldHistoricalData";

        public static TimeSpan MarketStartTime = new TimeSpan(9, 30, 0);
        public static TimeSpan MarketEndTime = new TimeSpan(16, 00, 0);
        public static (TimeSpan, TimeSpan) MarketDayTimeRange = (MarketStartTime, MarketEndTime);

        public static bool IsWeekend(this DateTime dt) => dt.DayOfWeek == DayOfWeek.Sunday || dt.DayOfWeek == DayOfWeek.Saturday;

        // Doesn't take holidays into account
        public static bool IsMarketOpen()
        {
            var now = DateTime.Now;
            var timeOfday = now.TimeOfDay;
            return !now.IsWeekend() && timeOfday > MarketStartTime && timeOfday < MarketEndTime;
        }

        public static IEnumerable<(DateTime, DateTime)> GetMarketDays(DateTime start, DateTime end)
        {
            if (end <= start)
                yield break;

            DateTime marketStartTime = new DateTime(start.Date.Ticks + MarketStartTime.Ticks);
            DateTime marketEndTime = new DateTime(start.Date.Ticks + MarketEndTime.Ticks);

            if (start < marketStartTime)
                start = marketStartTime;

            int i = 0;
            DateTime current = start;
            while (current < end)
            {
                if (!IsWeekend(current))
                {
                    if (i == 0 && start < marketEndTime)
                        yield return (start, marketEndTime);
                    else if (i > 0)
                        yield return (marketStartTime, marketEndTime);
                }

                current = current.AddDays(1);
                marketStartTime = marketStartTime.AddDays(1);
                marketEndTime = marketEndTime.AddDays(1);
                i++;
            }

            if (!IsWeekend(end) && end > marketStartTime)
            {
                if (end > marketEndTime)
                    end = marketEndTime;

                yield return (marketStartTime, end);
            }
        }

        public static string MakeDailyDataPath<TData>(string ticker, DateTime date) where TData : IMarketData, new()
        {
            return Path.Combine(ticker, typeof(TData).Name, date.ToString("yyyy-MM-dd"), "full.json");
        }

        public static string MakeDataPath<TData>(string ticker, DateTime date) where TData : IMarketData, new()
        {
            return Path.Combine(ticker, typeof(TData).Name, date.ToString("yyyy-MM-dd"), $"{date.ToString("yyyy-MM-dd HH-mm-ss")}.json");
        }

        public static IEnumerable<TData> DeserializeData<TData>(string path) where TData : IMarketData, new()
        {
            var json = File.ReadAllText(path);
            return (IEnumerable<TData>)JsonSerializer.Deserialize(json, typeof(IEnumerable<TData>));
        }

        public static IEnumerable<TData> DeserializeData<TData>(string rootDir, string symbol, DateTime date) where TData : IMarketData, new()
        {
            return DeserializeData<TData>(Path.Combine(rootDir, MakeDailyDataPath<TData>(symbol, date)));
        }

        public static void SerializeData<TData>(string path, IEnumerable<TData> data) where TData : IMarketData, new()
        {
            if(data == null)
                throw new ArgumentNullException(nameof(data));

            var dirPath = Path.GetDirectoryName(path);
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            var json = JsonSerializer.Serialize(data);
            File.WriteAllText(path, json);
        }

        public static Bar MakeBar(IEnumerable<Bar> bars, BarLength barLength)
        {
            Bar newBar = new Bar() { High = double.MinValue, Low = double.MaxValue, BarLength = barLength };

            int i = 0;
            int nbBars = bars.Count();
            foreach (Bar bar in bars)
            {
                if (i == 0)
                {
                    newBar.Open = bar.Open;
                    newBar.Time = bar.Time;
                }

                newBar.High = Math.Max(bar.High, bar.High);
                newBar.Low = Math.Min(bar.Low, bar.Low);
                newBar.Volume += bar.Volume;
                newBar.TradeAmount += bar.TradeAmount;

                if (i == nbBars - 1)
                {
                    newBar.Close = bar.Close;
                }

                i++;
            }

            return newBar;
        }

        internal class HistoricalDataFetcher
        {
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

            internal class FetcherErrorHandler : IBBrokerErrorHandler
            {
                HistoricalDataFetcher _fetcher;
                public FetcherErrorHandler(HistoricalDataFetcher fetcher, IBBroker broker, ILogger logger) : base(broker, logger)
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

            public HistoricalDataFetcher(IBBroker broker, ILogger logger)
            {
                _broker = broker ?? new IBBroker(321);
                _logger = logger;
            }

            public async Task<IEnumerable<TData>> GetDataForDay<TData>(DateTime date, (TimeSpan, TimeSpan) timeRange, Contract contract, DbCommandFactory<TData> commandFactory) where TData : IMarketData, new()
            {
                DateTime morning = new DateTime(date.Date.Ticks + timeRange.Item1.Ticks);
                DateTime current = new DateTime(date.Date.Ticks + timeRange.Item2.Ticks);

                _logger?.Info($"Getting data for {contract.Symbol} on {date.ToShortDateString()} ({morning.ToShortTimeString()} to {current.ToShortTimeString()})");

                // In order to respect TWS limitationsm, data is retrieved in chunks of 30 minutes for bars of 1 sec length (1800 bars total), from the end of the
                // time range to the beginning. 
                // https://interactivebrokers.github.io/tws-api/historical_limitations.html

                IEnumerable<TData> dailyData = Enumerable.Empty<TData>();
                while (current >= morning)
                {
                    var begin = current.AddMinutes(-30);
                    var end = current;
                    var existsCmd = commandFactory.CreateExistsCommand(contract.Symbol, current.Date, (begin.TimeOfDay, end.TimeOfDay));

                    IEnumerable<TData> data;
                    if (existsCmd.Execute())
                    {
                        var selectCmd = commandFactory.CreateSelectCommand(contract.Symbol, current.Date, (begin.TimeOfDay, end.TimeOfDay));
                        data = selectCmd.Execute();
                        var dateStr = current.Date.ToShortDateString();
                        _logger?.Info($"Data for {contract.Symbol} {dateStr} ({begin.ToShortTimeString()}-{end.ToShortTimeString()}) already exists in db. Skipping.");
                    }
                    else
                    {
                        data = await FetchHistoricalData<TData>(contract, current);
                        var insertCmd = commandFactory.CreateInsertCommand(contract.Symbol, data);
                        insertCmd.Execute();
                    }
                    
                    dailyData = data.Concat(dailyData);
                    current = current.AddMinutes(-30);
                }

                return dailyData;
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
                    _logger?.Info($"60 requests made : waiting 10 minutes...");
                    Wait10Minutes();
                    _nbRequest = 0;
                }
                else if (_nbRequest != 0 && _nbRequest % 5 == 0)
                {
                    _logger?.Info($"{NbRequest} requests made : waiting 2 seconds...");
                    Task.Delay(2000).Wait();
                }
            }

            void Wait10Minutes()
            {
                for (int i = 0; i < 10; ++i)
                {
                    Task.Delay(60 * 1000).Wait();
                    if (i < 9)
                        _logger?.Info($"{9 - i} minutes left...");
                    else
                        _logger?.Info($"Resuming historical data fetching");
                }
            }

            public class MarketHolidayException : Exception { }

            async Task<IEnumerable<TData>> FetchHistoricalData<TData>(Contract contract, DateTime time) where TData : IMarketData, new()
            {
                IEnumerable<TData> data = await Fetch<TData>(contract, time);
                if (IsPossibleMarketHoliday(time, data))
                {
                    _logger?.Info($"Possible market holiday on {time} (returned data time mismatch). Skipping.");
                    throw new MarketHolidayException();
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

            async Task<IEnumerable<TData>> Fetch<TData>(Contract contract, DateTime time) where TData : IMarketData, new()
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

            private async Task<IEnumerable<TData>> FetchBidAsk<TData>(Contract contract, DateTime time) where TData : IMarketData, new()
            {
                _logger?.Info($"Retrieving bid ask from TWS for '{contract.Symbol} {time}'.");

                // max nb of ticks per request is 1000 so we need to do multiple requests for 30 minutes...
                // There doesn't seem to be a way to convert ticks to seconds...
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

            private async Task<IEnumerable<TData>> FetchBars<TData>(Contract contract, DateTime time) where TData : IMarketData, new()
            {
                _logger?.Info($"Retrieving bars from TWS for '{contract.Symbol} {time}'.");
                var bars = await _broker.GetHistoricalDataAsync(contract, BarLength._1Sec, time, 1800);
                NbRequest++;
                return bars.Cast<TData>();
            }
        }
    }
}
