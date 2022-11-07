using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using System.Threading.Tasks;
using TradingBot.Broker.Client.Messages;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Broker;
using TradingBot.Utils.Db.DbCommandFactories;

namespace TradingBot.Utils.MarketData
{
    internal class MarketHolidayException : Exception { }

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

            _logger?.Info($"Getting {typeof(TData).Name}s for {contract.Symbol} on {date.ToShortDateString()} ({morning.ToShortTimeString()} to {current.ToShortTimeString()})");

            // In order to respect TWS limitationsm, data is retrieved in chunks of 30 minutes for bars of 1 sec length (1800 bars total), from the end of the
            // time range to the beginning. 
            // https://interactivebrokers.github.io/tws-api/historical_limitations.html

            IEnumerable<TData> dailyData = Enumerable.Empty<TData>();
            while (current > morning)
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
                    _logger?.Info($"{typeof(TData).Name}s for {contract.Symbol} {dateStr} ({begin.ToShortTimeString()}-{end.ToShortTimeString()}) already exists in db. Skipping.");
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

        bool IsPossibleMarketHoliday<TData>(DateTime time, IEnumerable<TData> data) where TData : IMarketData
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
                var bars = await FetchBars(contract, time);
                return bars.Cast<TData>();
            }
            else if (typeof(TData) == typeof(BidAsk))
            {
                var ba = await FetchTickData<BidAsk>(contract, time);
                return ba.Cast<TData>();
            }
            else if (typeof(TData) == typeof(Last))
            {
                var last = await FetchTickData<Last>(contract, time);
                return last.Cast<TData>();
            }
            else throw new NotImplementedException();
        }

        private async Task<IEnumerable<TData>> FetchTickData<TData>(Contract contract, DateTime time) where TData : IMarketData, new()
        {
            var type = typeof(TData);

            _logger?.Info($"Retrieving {type.Name} from TWS for '{contract.Symbol} {time}'.");

            // max nb of ticks per request is 1000 so we need to do multiple requests for 30 minutes...
            // There doesn't seem to be a way to convert ticks to seconds...
            // So we just do requests as long as we don't have 30 minutes.
            IEnumerable<IMarketData> tickData = Enumerable.Empty<IMarketData>();
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

                IEnumerable<IMarketData> ticks = null;
                if (typeof(TData) == typeof(BidAsk))
                {
                    ticks = await _broker.GetHistoricalBidAsksAsync(contract, current, tickCount);
                    
                    // Note that when BID_ASK historical data is requested, each request is counted twice according to the doc
                    NbRequest++;

                }
                else if (typeof(TData) == typeof(Last))
                {
                    ticks = await _broker.GetHistoricalLastsAsync(contract, current, tickCount);
                }

                NbRequest++;
                if (IsPossibleMarketHoliday(current, ticks))
                    return Enumerable.Empty<TData>();

                tickData = ticks.Concat(tickData);
                current = ticks.First().Time;

                diff = time - current;
            }

            // Remove out of range data.

            var timeOfDay = (time - _30min).TimeOfDay;
            return tickData.SkipWhile(d => d.Time.TimeOfDay < timeOfDay).Cast<TData>();
        }

        private async Task<IEnumerable<Bar>> FetchBars(Contract contract, DateTime time)
        {
            _logger?.Info($"Retrieving bars from TWS for '{contract.Symbol} {time}'.");
            var bars = await _broker.GetHistoricalBarsAsync(contract, BarLength._1Sec, time, 1800);
            NbRequest++;
            return bars;
        }
    }
}
