using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TradingBot.Broker;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Utils;

namespace Backtester
{
    internal class Backtester
    {
        const int TimeScale = 1;

        DateTime _start;
        DateTime _end;
        IBClient _client;
        ILogger _logger;
        Contract _contract;

        Dictionary<int, PnL> PnLs = new Dictionary<int, PnL>();
        Dictionary<DateTime, LinkedList<Bar>> _historicalData = new Dictionary<DateTime, LinkedList<Bar>>();

        public Backtester(Contract contract, DateTime from, DateTime to, ILogger logger)
        {
            _contract = contract;
            _start = from;
            _end = to;
            _logger = logger;
            _client = new IBClient(logger);
        }

        bool IsWeekend(DateTime dt) => dt.DayOfWeek == DayOfWeek.Sunday || dt.DayOfWeek == DayOfWeek.Saturday;

        public IEnumerable<(DateTime, DateTime)> GetMarketDays(DateTime start, DateTime end)
        {
            if (end <= start)
                yield break;

            DateTime marketStartTime = new DateTime(start.Year, start.Month, start.Day, 9, 30, 0);
            DateTime marketEndTime = new DateTime(start.Year, start.Month, start.Day, 16, 0, 0);

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

        public void FetchHistoricalData()
        {
            var marketDays = GetMarketDays(_start, _end);
            foreach (var day in marketDays)
            {
                //var barList = _client.GetHistoricalDataForDayAsync(_contract, day.Item2).Result;
                //_historicalData.Add(day.Item1, barList);
            }
        }

        public void Start()
        {

        }

        public void Stop()
        {

        }
    }
}
