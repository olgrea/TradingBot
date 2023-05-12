namespace TradingBotV2.Utils
{
    public record struct DateRange(DateTime From, DateTime To);

    public static class DateTimeUtils
    {
        public static DateTime Floor(this DateTime dt, TimeSpan span)
        {
            var ticks = dt.Ticks / span.Ticks;
            return new DateTime(ticks * span.Ticks, dt.Kind);
        }

        public static DateTime Ceiling(this DateTime dt, TimeSpan span)
        {
            long ticks = (dt.Ticks + span.Ticks) / span.Ticks;
            return new DateTime(ticks * span.Ticks, dt.Kind);
        }

        public static long ToUnixTimeSeconds(this DateTime dt)
        {
            return new DateTimeOffset(dt).ToUnixTimeSeconds();
        }
    }
}
