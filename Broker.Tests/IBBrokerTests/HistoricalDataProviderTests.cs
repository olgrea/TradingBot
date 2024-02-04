using System.Diagnostics;
using Broker.IBKR;
using Broker.MarketData;
using NLog;
using NUnit.Framework;
using Broker.Tests;
using Broker.Utils;
using Broker.IBKR.Providers;

namespace IBBrokerTests
{
    internal class HistoricalOneSecBarsTests : HistoricalDataProviderTests<Bar> {}
    internal class HistoricalBidAskTests : HistoricalDataProviderTests<BidAsk> {}
    internal class HistoricalLastsTests : HistoricalDataProviderTests<Last> {}

    abstract class HistoricalDataProviderTests<TData> where TData : IMarketData, new()
    {
        ILogger? _logger;
        IBBroker _broker;
        IBHistoricalDataProvider _historicalProvider;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            TestsUtils.RestoreTestDb();
            _logger = TestsUtils.CreateLogger();
            _broker = TestsUtils.CreateBroker(_logger);
            _historicalProvider = (IBHistoricalDataProvider)_broker.HistoricalDataProvider;
            await _broker.ConnectAsync();
        }

        [SetUp]
        public async Task SetUp()
        {
            TestsUtils.RestoreTestDb();
            Debug.Assert(_historicalProvider.DbPath == TestsUtils.TestDbPath);
            _historicalProvider.DbEnabled = true;
            _historicalProvider.CacheEnabled = true;
            _historicalProvider.ClearCache();
            
            _logger?.PrintCurrentTestName();
            await Task.CompletedTask;
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDown()
        {
            await Task.Delay(50);
            await _broker.DisconnectAsync();
            await Task.Delay(50);
        }

        // Test data in Db (GME) : 
        // 2024/01/01 : holiday
        // 2024/01/02 : full day (9:30 to 16:00)
        // 2024/01/03 : full day
        // 2024/01/04 : full day
        // 2024/01/05 : 10:00 to 11:00

        const string Ticker = "GME";
        static DateOnly Holiday = new DateOnly(2024, 01, 01);
        static DateOnly FullDay1 = new DateOnly(2024, 01, 02);
        static DateOnly FullDay2 = new DateOnly(2024, 01, 03);
        static DateOnly FullDay3 = new DateOnly(2024, 01, 04);
        static DateRange PartialDay = (new DateTime(2024, 01, 05, 10, 00, 00), new DateTime(2024, 01, 05, 11, 00, 00));

        public static IEnumerable<(string, DateRange)> GetRequiredTestData()
        {
            yield return (Ticker, FullDay1.ToMarketHours());
            yield return (Ticker, FullDay2.ToMarketHours());
            yield return (Ticker, FullDay3.ToMarketHours());
            yield return (Ticker, PartialDay);
        }

        [Test]
        public async Task GetHistoricalData_Holiday_ShouldThrow()
        {
            string ticker = Ticker;
            DateOnly date = Holiday;

            Assert.ThrowsAsync<ArgumentException>(async () => await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, date));
            await Task.CompletedTask;
        }

        [Test]
        public async Task GetHistoricalData_SpecificDate()
        {
            string ticker = Ticker;
            DateOnly date = FullDay1;

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, date);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.IsTrue(results.All(r => DateOnly.FromDateTime(r.Time.Date) == date));
        }

        [Test]
        public async Task GetHistoricalData_DateRange_IntraDay()
        {
            string ticker = Ticker;
            DateOnly day = FullDay1;
            DateTime from = new DateTime(day.Year, day.Month, day.Day, 10, 13, 00);
            DateTime to = new DateTime(day.Year, day.Month, day.Day, 11, 47, 53);

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.IsTrue(results.All(r => r.Time >= from && r.Time < to));
        }

        [Test]
        public async Task GetHistoricalData_DateRange_MultipleDays()
        {
            string ticker = Ticker;
            DateTime from = new DateTime(FullDay1.Year, FullDay1.Month, FullDay1.Day, 12, 0, 0);
            DateTime to = new DateTime(FullDay3.Year, FullDay3.Month, FullDay3.Day, 10, 30, 0);

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.IsTrue(results.All(r => r.Time >= from && r.Time < to));
        }

        [Test]
        public async Task GetHistoricalData_CacheDisabled_DbDisabled_RetrievesThemFromIBKR()
        {
            string ticker = Ticker;
            DateOnly day = FullDay1;
            DateTime from = new DateTime(day.Year, day.Month, day.Day, 10, 00, 00);
            DateTime to = new DateTime(day.Year, day.Month, day.Day, 11, 00, 00);
            _historicalProvider.CacheEnabled = false;
            _historicalProvider.DbEnabled = false;

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.Greater(_historicalProvider._lastOperationStats.NbRetrievedFromIBKR, 0);
        }

        [Test]
        public async Task GetHistoricalData_CacheDisabled_DbEnabled_RetrievesThemFromDb()
        {
            string ticker = Ticker;
            DateOnly day = FullDay1;
            DateTime from = new DateTime(day.Year, day.Month, day.Day, 10, 00, 00);
            DateTime to = new DateTime(day.Year, day.Month, day.Day, 11, 00, 00);
            _historicalProvider.CacheEnabled = false;

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.Greater(_historicalProvider._lastOperationStats.NbRetrievedFromDb, 0);
        }

        [Test]
        public async Task GetHistoricalData_CacheDisabled_DbEnabled_DataNotInDb_InsertsItInDb()
        {
            string ticker = Ticker;
            DateTime day = PartialDay.From.Date;
            DateTime from = new DateTime(day.Year, day.Month, day.Day, 13, 00, 00);
            DateTime to = new DateTime(day.Year, day.Month, day.Day, 14, 00, 00);
            TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            try
            {
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.Greater(_historicalProvider._lastOperationStats.NbInsertedInDb, 0);
            }
            finally
            {
                TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));
            }
        }

        [Test]
        public async Task GetHistoricalData_CacheEnabled_DataNotInCache_InsertsItInCache()
        {
            string ticker = Ticker;
            DateTime day = PartialDay.From.Date;
            DateTime from = new DateTime(day.Year, day.Month, day.Day, 13, 00, 00);
            DateTime to = new DateTime(day.Year, day.Month, day.Day, 14, 00, 00);
            TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));
            _historicalProvider.CacheEnabled = true;
            _historicalProvider.ClearCache();

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            try
            {
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.Greater(_historicalProvider._lastOperationStats.NbInsertedInCache, 0);
            }
            finally
            {
                TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));
            }
        }

        [Test]
        public async Task GetHistoricalData_CacheEnabled_DataInCache_RetrievesItFromCache()
        {
            string ticker = Ticker;
            DateTime day = PartialDay.From.Date;
            DateTime from = new DateTime(day.Year, day.Month, day.Day, 13, 00, 00);
            DateTime to = new DateTime(day.Year, day.Month, day.Day, 14, 00, 00);
            TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));
            _historicalProvider.CacheEnabled = true;

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            try
            {
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.Greater(_historicalProvider._lastOperationStats.NbInsertedInCache, 0);

                results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.Greater(_historicalProvider._lastOperationStats.NbRetrievedFromCache, 0);
            }
            finally
            {
                TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));
            }
        }

        [Test]
        public async Task GetHistoricalData_CacheDisabled_DbDisabled_DataNotInDbOrCache_DoesntInsertsItAnywhere()
        {
            string ticker = Ticker;
            DateTime day = PartialDay.From.Date;
            DateTime from = new DateTime(day.Year, day.Month, day.Day, 13, 00, 00);
            DateTime to = new DateTime(day.Year, day.Month, day.Day, 14, 00, 00);
            TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));
            _historicalProvider.CacheEnabled = false;
            _historicalProvider.DbEnabled = false;

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            try
            {
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.AreEqual(0, _historicalProvider._lastOperationStats.NbInsertedInCache);
                Assert.AreEqual(0, _historicalProvider._lastOperationStats.NbInsertedInDb);
            }
            finally
            {
                TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));
            }
        }
    }
}
