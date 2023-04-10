namespace TradingBotV2.Broker.MarketData
{
    public static class MarketDataUtils
    {
        public static TimeSpan MarketStartTime = new TimeSpan(9, 30, 0);
        public static TimeSpan MarketEndTime = new TimeSpan(16, 00, 0);
        public static (TimeSpan, TimeSpan) MarketDayTimeRange = (MarketStartTime, MarketEndTime);
        public const string TWSTimeFormat = "yyyyMMdd  HH:mm:ss";

        // https://www.nyse.com/markets/hours-calendars
        static HashSet<DateTime> MarketHolidays = new HashSet<DateTime>()
        {
            new DateTime(2023, 01, 02), new DateTime(2024, 01, 01), new DateTime(2025, 01, 01),
            new DateTime(2023, 01, 16), new DateTime(2024, 01, 15), new DateTime(2025, 01, 20),
            new DateTime(2023, 02, 20), new DateTime(2024, 02, 19), new DateTime(2025, 02, 17),
            new DateTime(2023, 04, 07), new DateTime(2024, 03, 29), new DateTime(2025, 04, 18),
            new DateTime(2023, 05, 29), new DateTime(2024, 05, 27), new DateTime(2025, 05, 26),
            new DateTime(2023, 06, 19), new DateTime(2024, 06, 19), new DateTime(2025, 06, 19),
            new DateTime(2023, 07, 04), new DateTime(2024, 07, 04), new DateTime(2025, 07, 04),
            new DateTime(2023, 09, 04), new DateTime(2024, 09, 02), new DateTime(2025, 09, 01),
            new DateTime(2023, 11, 23), new DateTime(2024, 11, 28), new DateTime(2025, 11, 27),
            new DateTime(2023, 12, 25), new DateTime(2024, 12, 25), new DateTime(2025, 12, 25),
        };

        public static bool IsMarketHoliday(DateTime date)
        {
            return MarketHolidays.Contains(date.Date);
        }

        public static bool IsWeekend(this DateTime dt) => dt.DayOfWeek == DayOfWeek.Sunday || dt.DayOfWeek == DayOfWeek.Saturday;

        public static bool IsMarketOpen()
        {
            return WasMarketOpen(DateTime.Now);
        }

        public static bool WasMarketOpen(DateTime date)
        {
            var timeOfday = date.TimeOfDay;
            return !IsMarketHoliday(date) && !date.IsWeekend() && timeOfday >= MarketStartTime && timeOfday <= MarketEndTime;
        }

        public static (DateTime, DateTime) ToMarketHours(this DateTime date)
        {
            return (new DateTime(date.Date.Ticks + MarketStartTime.Ticks), new DateTime(date.Date.Ticks + MarketEndTime.Ticks));
        }

        public static IEnumerable<(DateTime, DateTime)> GetMarketDays(DateTime start, DateTime end)
        {
            if (end <= start)
                yield break;

            if (start.TimeOfDay < MarketStartTime)
                start = new DateTime(start.Date.Ticks + MarketStartTime.Ticks);

            DateTime current = start;
            while (end - current > TimeSpan.FromDays(1)) 
            {
                if (WasMarketOpen(current))
                {
                    yield return (current, new DateTime(current.Date.Ticks + MarketEndTime.Ticks));
                }
                current = new DateTime(current.AddDays(1).Date.Ticks + MarketStartTime.Ticks);
            }

            if(current < end & WasMarketOpen(current) && end.TimeOfDay > MarketStartTime)
            {
                yield return (current, new DateTime(current.Date.Ticks + Math.Min(MarketEndTime.Ticks, end.TimeOfDay.Ticks)));
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
