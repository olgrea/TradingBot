using System.Diagnostics;
using Microsoft.Data.Sqlite;
using NLog;
using NUnit.Framework;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.IBKR;
using TradingBotV2.Tests;
using TradingBotV2.Utils;

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
            _logger = TestsUtils.CreateLogger();
            _broker = TestsUtils.CreateBroker(_logger);
            _historicalProvider = (IBHistoricalDataProvider)_broker.HistoricalDataProvider;
            await _broker.ConnectAsync();
        }

        [SetUp]
        public async Task SetUp()
        {
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
        // 2023/04/03 : full day (9:30 to 16:00)
        // 2023/04/04 : full day
        // 2023/04/05 : full day
        // 2023/04/06 : 10:00 to 11:00
        // 2023/04/07 : holiday

        [Test]
        public async Task GetHistoricalData_Holiday_ShouldThrow()
        {
            string ticker = "GME";
            DateOnly date = new DateOnly(2023, 04, 07);

            Assert.ThrowsAsync<ArgumentException>(async () => await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, date));
            await Task.CompletedTask;
        }

        [Test]
        public async Task GetHistoricalData_SpecificDate()
        {
            string ticker = "GME";
            DateOnly date = new DateOnly(2023, 04, 03);

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, date);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.IsTrue(results.All(r => DateOnly.FromDateTime(r.Time.Date) == date));
        }

        [Test]
        public async Task GetHistoricalData_DateRange_IntraDay()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 10, 13, 00);
            DateTime to = new DateTime(2023, 04, 03, 11, 47, 53);

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.IsTrue(results.All(r => r.Time >= from && r.Time < to));
        }

        [Test]
        public async Task GetHistoricalData_DateRange_MultipleDays()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 12, 0, 0);
            DateTime to = new DateTime(2023, 04, 05, 10, 30, 0);

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.IsTrue(results.All(r => r.Time >= from && r.Time < to));
        }

        [Test]
        public async Task GetHistoricalData_CacheDisabled_DbDisabled_RetrievesThemFromIBKR()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 10, 0, 0);
            DateTime to = new DateTime(2023, 04, 03, 11, 0, 0);
            _historicalProvider.CacheEnabled = false;
            _historicalProvider.DbEnabled = false;

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.Greater(_historicalProvider._nbRetrievedFromIBKR, 0);
        }

        [Test]
        public async Task GetHistoricalData_CacheDisabled_DbEnabled_RetrievesThemFromDb()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 10, 0, 0);
            DateTime to = new DateTime(2023, 04, 03, 11, 0, 0);
            _historicalProvider.CacheEnabled = false;

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.Greater(_historicalProvider._nbRetrievedFromDb, 0);
        }

        [Test]
        public async Task GetHistoricalData_CacheDisabled_DbEnabled_DataNotInDb_InsertsItInDb()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 06, 13, 0, 0);
            DateTime to = new DateTime(2023, 04, 06, 14, 0, 0);
            TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            try
            {
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.Greater(_historicalProvider._nbInsertedInDb, 0);
            }
            finally
            {
                TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));
            }
        }

        [Test]
        public async Task GetHistoricalData_CacheEnabled_DataNotInCache_InsertsItInCache()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 06, 13, 0, 0);
            DateTime to = new DateTime(2023, 04, 06, 14, 0, 0);
            TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));
            _historicalProvider.CacheEnabled = true;
            _historicalProvider.ClearCache();

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            try
            {
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.Greater(_historicalProvider._nbInsertedInCache, 0);
            }
            finally
            {
                TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));
            }
        }

        [Test]
        public async Task GetHistoricalData_CacheEnabled_DataInCache_RetrievesItFromCache()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 06, 13, 0, 0);
            DateTime to = new DateTime(2023, 04, 06, 14, 0, 0);
            TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));
            _historicalProvider.CacheEnabled = true;

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            try
            {
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.Greater(_historicalProvider._nbInsertedInCache, 0);

                results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.Greater(_historicalProvider._nbRetrievedFromCache, 0);
            }
            finally
            {
                TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));
            }
        }

        [Test]
        public async Task GetHistoricalData_CacheDisabled_DbDisabled_DataNotInDbOrCache_DoesntInsertsItAnywhere()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 06, 13, 0, 0);
            DateTime to = new DateTime(2023, 04, 06, 14, 0, 0);
            TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));
            _historicalProvider.CacheEnabled = false;
            _historicalProvider.DbEnabled = false;

            var results = await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, from, to);

            try
            {
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.AreEqual(0, _historicalProvider._nbInsertedInCache);
                Assert.AreEqual(0, _historicalProvider._nbInsertedInDb);
            }
            finally
            {
                TestsUtils.DeleteDataInTestDb<TData>(ticker, new(from, to));
            }
        }

        async Task FillTestDataDb()
        {
            string ticker = "GME";

            var path = _historicalProvider.DbPath;
            try
            {
                _historicalProvider.DbPath = TestsUtils.TestDataDbPath;
                await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, new DateTime(2023, 04, 03));
                await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, new DateTime(2023, 04, 04));
                await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, new DateTime(2023, 04, 05));
                await _historicalProvider.GetHistoricalDataAsync<TData>(ticker, new DateTime(2023, 04, 06, 10, 0, 0), new DateTime(2023, 04, 06, 11, 0, 0));
            }
            finally
            {
                _historicalProvider.DbPath = path;
            }
        }
    }
}
