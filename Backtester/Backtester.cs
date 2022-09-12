using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Backtester.Client;
using MathNet.Numerics.LinearAlgebra.Factorization;
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

        TWSClientSlim _client;
        ILogger _logger;

        Dictionary<int, PnL> PnLs = new Dictionary<int, PnL>();

        public Backtester(DateTime from, DateTime to, ILogger logger)
        {
            _start = from;
            _end = to;
            _logger = logger;
            _client = new TWSClientSlim(logger);
        }

        public void Start()
        {

        }

        public void Stop()
        {

        }

        bool IsWeekend(DateTime dt) => dt.DayOfWeek == DayOfWeek.Sunday || dt.DayOfWeek == DayOfWeek.Saturday;

        public IEnumerable<(DateTime, DateTime)> GetMarketDays(DateTime start, DateTime end)
        {
            if (end <= start)
                yield break;

            DateTime marketStartTime = new DateTime(start.Year, start.Month, start.Day, 9, 30, 0);
            DateTime marketEndTime = new DateTime(start.Year, start.Month, start.Day, 16, 0, 0); 

            if(start < marketStartTime)
                start = marketStartTime;

            int i = 0;
            DateTime current = start;
            while (current < end)
            {
                if(!IsWeekend(current))
                {
                    if(i==0 && start < marketEndTime)
                        yield return (start, marketEndTime);
                    else if(i > 0)
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
            var marketDays = GetMarketDays(_start, _end).ToList();
        }

        //public void GetMarketDays()
        //{
        //    var ranges = SplitByDay(_start, _end).Where(r => r.Item1.DayOfWeek != DayOfWeek.Sunday && r.Item1.DayOfWeek != DayOfWeek.Saturday).ToList();

        //    if (ranges[0].Item1.Hour >= 16)
        //        ranges.RemoveAt(0);

        //    if (ranges[ranges.Count-1].Item2.Hour < 9 && ranges[ranges.Count - 1].Item2.Minute < 30)
        //        ranges.RemoveAt(ranges.Count - 1);

        //    for (int i = 0; i < ranges.Count-1 ; ++i)
        //    {
        //        ranges[i] = (ranges[i].Item1, new DateTime(ranges[i].Item1.Year, ranges[i].Item1.Month, ranges[i].Item1.Day, 16, 0, 0));
        //    }

        //    for (int i = 1; i < ranges.Count; ++i)
        //    {
        //        ranges[i] = (new DateTime(ranges[i].Item2.Year, ranges[i].Item2.Month, ranges[i].Item2.Day, 9, 30, 0), ranges[i].Item2);
        //    }
        //}

        //IEnumerable<(DateTime, DateTime)> SplitByDay(DateTime start, DateTime end)
        //{
        //    DateTime chunkEnd;
        //    while ((chunkEnd = start.AddDays(1)) < end)
        //    {
        //        yield return (start, chunkEnd);
        //        start = chunkEnd;
        //    }
        //    yield return (start, end);
        //}
    }
}
