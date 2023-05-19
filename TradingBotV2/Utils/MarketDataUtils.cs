using TradingBotV2.Broker.MarketData;

namespace TradingBotV2.Utils
{
    public static class MarketDataUtils
    {
        public static TimeSpan PreMarketStartTime = new TimeSpan(4, 00, 00);
        public static TimeSpan PreMarketEndTime = new TimeSpan(9, 30, 0);
        public static TimeSpan MarketStartTime = new TimeSpan(9, 30, 0);
        public static TimeSpan MarketEndTime = new TimeSpan(16, 00, 0);
        public static TimeSpan AfterHoursStartTime = new TimeSpan(16, 00, 0);
        public static TimeSpan AfterHoursEndTime = new TimeSpan(20, 00, 0);

        public static (TimeSpan, TimeSpan) MarketDayTimeRange = (MarketStartTime, MarketEndTime);
        public static (TimeSpan, TimeSpan) PreMarketDayTimeRange = (PreMarketStartTime, PreMarketEndTime);
        public static (TimeSpan, TimeSpan) AfterHoursTimeRange = (AfterHoursStartTime, AfterHoursEndTime);
        public static (TimeSpan, TimeSpan) ExtendedHoursTimeRange = (PreMarketStartTime, AfterHoursEndTime);
        public const string TWSTimeFormat = "yyyyMMdd HH:mm:ss";

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

        public static DateOnly FindLastOpenDay(DateTime date, bool extendedHours = false)
        {
            var openDay = DateOnly.FromDateTime(date);
            while (!WasMarketOpen(openDay, extendedHours))
                openDay = openDay.AddDays(-1);
            return openDay;
        }

        public static bool IsMarketHoliday(DateTime date)
        {
            return MarketHolidays.Contains(date.Date);
        }

        public static bool IsWeekend(this DateTime dt) => dt.DayOfWeek == DayOfWeek.Sunday || dt.DayOfWeek == DayOfWeek.Saturday;

        public static bool IsMarketOpen(bool extendedHours = false)
        {
            return WasMarketOpen(DateTime.Now, extendedHours);
        }

        public static bool WasMarketOpen(DateOnly date, bool extendedHours = false)
        {
            var startTime = extendedHours ? PreMarketStartTime : MarketStartTime;
            return WasMarketOpen(date.ToDateTime(new TimeOnly(startTime.Hours, startTime.Minutes, startTime.Seconds)), extendedHours);
        }

        public static bool WasMarketOpen(DateTime date, bool extendedHours = false)
        {
            var timeOfday = date.TimeOfDay;
            var (startTime, endTime) = extendedHours ? ExtendedHoursTimeRange : MarketDayTimeRange;
            return !IsMarketHoliday(date) && !date.IsWeekend() && timeOfday >= startTime && timeOfday <= endTime;
        }

        public static (DateTime, DateTime) ToMarketHours(this DateTime date, bool extendedHours = false)
        {
            var (startTime, endTime) = extendedHours ? ExtendedHoursTimeRange : MarketDayTimeRange;
            return (new DateTime(date.Date.Ticks + startTime.Ticks), new DateTime(date.Date.Ticks + endTime.Ticks));
        }

        public static (DateTime, DateTime) ToMarketHours(this DateOnly date, bool extendedHours = false)
        {
            var (startTime, endTime) = extendedHours ? ExtendedHoursTimeRange : MarketDayTimeRange;
            return (date.ToDateTime(TimeOnly.FromTimeSpan(startTime)), date.ToDateTime(TimeOnly.FromTimeSpan(endTime)));
        }

        public static IEnumerable<(DateTime, DateTime)> GetMarketDays(DateTime start, DateTime end, bool extendedHours = false)
        {
            var (startTime, endTime) = extendedHours ? ExtendedHoursTimeRange : MarketDayTimeRange;
            if (end <= start)
                yield break;

            if (start.TimeOfDay < startTime)
                start = new DateTime(start.Date.Ticks + startTime.Ticks);

            DateTime current = start;
            while (end - current > TimeSpan.FromDays(1))
            {
                if (WasMarketOpen(current, extendedHours))
                {
                    yield return (current, new DateTime(current.Date.Ticks + endTime.Ticks));
                }
                current = new DateTime(current.AddDays(1).Date.Ticks + startTime.Ticks);
            }

            if (current < end & WasMarketOpen(current, extendedHours) && end.TimeOfDay > startTime)
            {
                yield return (current, new DateTime(current.Date.Ticks + Math.Min(endTime.Ticks, end.TimeOfDay.Ticks)));
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
                newBar.NbTrades += bar.NbTrades;

                if (i == nbBars - 1)
                {
                    newBar.Close = bar.Close;
                }

                i++;
            }

            return newBar;
        }

        public static Bar? MakeBarFromLasts(IEnumerable<Last> lasts, BarLength barLength)
        {
            if (lasts == null || !lasts.Any())
                return null;

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
