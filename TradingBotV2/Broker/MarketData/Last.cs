using TradingBotV2.Utils;

namespace TradingBotV2.Broker.MarketData
{
    public class Last : IMarketData
    {
        public DateTime Time { get; set; }
        public double Price { get; set; }
        public int Size { get; set; }

        public static explicit operator Last(IBApi.Last last)
        {
            return new Last()
            {
                Price = last.Price,
                // TODO : update db schema. Loss of data is acceptable for now.
                Size = Convert.ToInt32(last.Size),
                Time = DateTimeOffset.FromUnixTimeSeconds(last.Time).DateTime.ToLocalTime(),
            };
        }

        public static explicit operator IBApi.Last(Last last)
        {
            return new IBApi.Last()
            {
                Price = last.Price,
                Size = last.Size,
                Time = last.Time.ToUnixTimeSeconds(),
            };
        }

        public static explicit operator Last(IBApi.HistoricalTickLast last)
        {
            return new Last()
            {
                Price = last.Price,
                Size = Convert.ToInt32(last.Size),
                Time = DateTimeOffset.FromUnixTimeSeconds(last.Time).DateTime.ToLocalTime(),
            };
        }

        public static explicit operator IBApi.HistoricalTickLast(Last last)
        {
            return new IBApi.HistoricalTickLast(
                last.Time.ToUnixTimeSeconds(),
                new IBApi.TickAttribLast(),
                last.Price,
                last.Size,
                "",
                ""
            );
        }
    }
}
