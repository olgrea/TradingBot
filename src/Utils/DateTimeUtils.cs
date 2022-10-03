using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Utils
{
    public static class DateTimeUtils
    {
        public static TimeSpan MarketStartTime = new TimeSpan(9, 30, 0);
        public static TimeSpan MarketEndTime = new TimeSpan(15, 55, 0);

        public static bool IsWeekend(this DateTime dt) => dt.DayOfWeek == DayOfWeek.Sunday || dt.DayOfWeek == DayOfWeek.Saturday;

        public static IEnumerable<(DateTime, DateTime)> GetMarketDays(DateTime start, DateTime end)
        {
            if (end <= start)
                yield break;

            DateTime marketStartTime = new DateTime(start.Year, start.Month, start.Day, MarketStartTime.Hours, MarketStartTime.Minutes, MarketStartTime.Seconds);
            DateTime marketEndTime = new DateTime(start.Year, start.Month, start.Day, MarketEndTime.Hours, MarketEndTime.Minutes, MarketEndTime.Seconds);

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
    }
}
