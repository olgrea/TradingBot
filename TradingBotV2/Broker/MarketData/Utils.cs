using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingBotV2.Broker.MarketData
{
    public static class MarketDataUtils
    {
        public static TimeSpan MarketStartTime = new TimeSpan(9, 30, 0);
        public static TimeSpan MarketEndTime = new TimeSpan(16, 00, 0);
        public static (TimeSpan, TimeSpan) MarketDayTimeRange = (MarketStartTime, MarketEndTime);
        public const string TWSTimeFormat = "yyyyMMdd  HH:mm:ss";

        public static bool IsWeekend(this DateTime dt) => dt.DayOfWeek == DayOfWeek.Sunday || dt.DayOfWeek == DayOfWeek.Saturday;

        // TODO : get yearly market holidays from a csv file or something
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

        public static Bar CombineBars(IEnumerable<Bar> bars, BarLength barLength)
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

                newBar.High = Math.Max(newBar.High, bar.High);
                newBar.Low = Math.Min(newBar.Low, bar.Low);
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

        public static Bar MakeBarFromLasts(IEnumerable<Last> lasts, BarLength barLength)
        {
            Bar newBar = new Bar() { High = double.MinValue, Low = double.MaxValue, BarLength = barLength };

            int i = 0;
            int count = lasts.Count();
            foreach (Last last in lasts)
            {
                if (i == 0)
                {
                    newBar.Open = last.Price;
                    newBar.Time = last.Time;
                }

                newBar.High = Math.Max(newBar.High, last.Price);
                newBar.Low = Math.Min(newBar.Low, last.Price);
                newBar.Volume += last.Size;

                if (i == count - 1)
                {
                    newBar.Close = last.Price;
                }

                i++;
            }

            return newBar;
        }
    }
}
