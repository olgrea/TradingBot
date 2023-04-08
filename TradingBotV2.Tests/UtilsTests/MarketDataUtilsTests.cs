using NUnit.Framework;
using TradingBotV2.Broker.MarketData;

namespace UtilsTests
{
    internal class MarketDataUtilsTests
    {
        [Test]
        public void GetMarketDays_SameDay_DuringMarketHours_ReturnsInputs()
        {
            DateTime start = new DateTime(2023, 04, 06, 10, 00, 00);
            DateTime end = new DateTime(2023, 04, 06, 11, 00, 00);

            var results = MarketDataUtils.GetMarketDays(start, end).ToList();

            Assert.IsNotEmpty(results);
            Assert.AreEqual(1, results.Count);
            Assert.Contains((start, end), results);
        }

        [Test]
        public void GetMarketDays_SameDay_OutsideMarketHours_ClampsToMarketHours()
        {
            DateTime start = new DateTime(2023, 04, 06, 7, 00, 00);
            DateTime end = new DateTime(2023, 04, 06, 19, 00, 00);

            var results = MarketDataUtils.GetMarketDays(start, end).ToList();

            Assert.IsNotEmpty(results);
            Assert.AreEqual(1, results.Count);

            DateTime expectedStart = new DateTime(2023, 04, 06, 9, 30, 00);
            DateTime expectedEnd = new DateTime(2023, 04, 06, 16, 00, 00);
            Assert.Contains((expectedStart, expectedEnd), results);
        }

        [Test]
        public void GetMarketDays_MultipleDays_InsideMarketHours_KeepsCorrectStartAndEndHours()
        {
            DateTime start = new DateTime(2023, 04, 04, 10, 00, 00);
            DateTime end = new DateTime(2023, 04, 06, 13, 00, 00);

            var results = MarketDataUtils.GetMarketDays(start, end).ToList();

            Assert.IsNotEmpty(results);
            Assert.AreEqual(3, results.Count);

            long marketStartTime = MarketDataUtils.MarketStartTime.Ticks;
            long marketEndTime = MarketDataUtils.MarketEndTime.Ticks;
            (DateTime, DateTime)[] expected = new (DateTime, DateTime)[3]
            {
                (start, new DateTime(start.Date.Ticks + marketEndTime)),
                (new DateTime(start.AddDays(1).Date.Ticks + marketStartTime), new DateTime(start.AddDays(1).Date.Ticks + marketEndTime)),
                (new DateTime(start.AddDays(2).Date.Ticks + marketStartTime), end),
            };

            foreach (var e in expected)
                Assert.Contains(e, results);
        }

        [Test]
        public void GetMarketDays_MultipleDays_SkipsWeekends()
        {
            DateTime start = new DateTime(2023, 03, 31, 7, 00, 00);
            DateTime end = new DateTime(2023, 04, 03, 19, 00, 00);

            var results = MarketDataUtils.GetMarketDays(start, end).ToList();

            Assert.IsNotEmpty(results);
            Assert.AreEqual(2, results.Count);

            long marketStartTime = MarketDataUtils.MarketStartTime.Ticks;
            long marketEndTime = MarketDataUtils.MarketEndTime.Ticks;
            Assert.Contains((new DateTime(start.Date.Ticks + marketStartTime), new DateTime(start.Date.Ticks + marketEndTime)), results);
            Assert.Contains((new DateTime(end.Date.Ticks + marketStartTime), new DateTime(end.Date.Ticks + marketEndTime)), results);
        }
    }
}
