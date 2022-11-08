using System;
using System.Collections.Generic;
using System.Linq;

namespace InteractiveBrokers.MarketData
{
    public static class Utils
    {
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
                if (!current.IsWeekend())
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

            if (!end.IsWeekend() && end > marketStartTime)
            {
                if (end > marketEndTime)
                    end = marketEndTime;

                yield return (marketStartTime, end);
            }
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
    }
}
