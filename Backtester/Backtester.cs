using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TradingBot.Broker;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Utils;

namespace Backtester
{
    internal class Backtester
    {
        const int TimeScale = 1;

        DateTime _start;
        DateTime _end;
        TWSClient _client;
        ILogger _logger;
        Contract _contract;

        Dictionary<int, PnL> PnLs = new Dictionary<int, PnL>();

        public Backtester(Contract contract, DateTime from, DateTime to, ILogger logger)
        {
            _contract = contract;
            _start = from;
            _end = to;
            _logger = logger;
            _client = new TWSClient(logger);
        }

        public void FetchHistoricalData()
        {
            var ranges = SplitDateRange(_start, _end).Where(r => r.Item1.DayOfWeek != DayOfWeek.Sunday && r.Item1.DayOfWeek != DayOfWeek.Saturday);
            foreach(var range in ranges)
            {
                //_client.GetHistoricalDataForDayAsync(_contract, );
            }
        }

        IEnumerable<(DateTime, DateTime)> SplitDateRange(DateTime start, DateTime end)
        {
            DateTime chunkEnd;
            while ((chunkEnd = start.AddDays(1)) < end)
            {
                if(chunkEnd.Hour > 16)
                    chunkEnd = new DateTime(chunkEnd.Year, chunkEnd.Month, chunkEnd.Day, 16, 0, 0, 0);

                yield return (start, chunkEnd);

                if (chunkEnd.Hour < 9 && chunkEnd.Minute < 30)
                    chunkEnd = new DateTime(chunkEnd.Year, chunkEnd.Month, chunkEnd.Day, 9, 30, 0, 0);

                start = chunkEnd;
            }
            yield return (start, end);
        }

        public void Start()
        {

        }

        public void Stop()
        {

        }
    }
}
