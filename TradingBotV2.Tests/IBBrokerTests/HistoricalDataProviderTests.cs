using System.Diagnostics;
using Microsoft.Data.Sqlite;
using NLog;
using NUnit.Framework;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.IBKR;

namespace IBBrokerTests
{
    internal class HistoricalDataProviderTests
    {
        public const string TestDbPath = @"C:\tradingbot\db\tests.sqlite3";

        // There are 23400 one sec bars between 9:30 and 16:00
        const int NbOfOneSecBarsInADay = 23400;

        IBBroker _broker;
        IBHistoricalDataProvider _historicalProvider;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            _broker = new IBBroker(9001);
            await _broker.ConnectAsync();
            
            _historicalProvider = (IBHistoricalDataProvider)_broker.HistoricalDataProvider;
            _historicalProvider.DbPath = TestDbPath;
        }

        [SetUp]
        public async Task SetUp()
        {
            Debug.Assert(_historicalProvider.DbPath == TestDbPath);
            _historicalProvider.EnableDb = true;
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
        
        //[Test]
        public async Task SetupTestData()
        {
            string ticker = "GME";

            //await _historicalProvider.GetHistoricalOneSecBarsAsync(ticker, new DateTime(2023, 04, 03));
            //await _historicalProvider.GetHistoricalOneSecBarsAsync(ticker, new DateTime(2023, 04, 04));
            //await _historicalProvider.GetHistoricalOneSecBarsAsync(ticker, new DateTime(2023, 04, 05));
            //await _historicalProvider.GetHistoricalOneSecBarsAsync(ticker, new DateTime(2023, 04, 06, 10, 0, 0), new DateTime(2023, 04, 06, 11, 0, 0));

            //await _historicalProvider.GetHistoricalBidAsksAsync(ticker, new DateTime(2023, 04, 03));
            //await _historicalProvider.GetHistoricalBidAsksAsync(ticker, new DateTime(2023, 04, 04));
            //await _historicalProvider.GetHistoricalBidAsksAsync(ticker, new DateTime(2023, 04, 05));
            //await _historicalProvider.GetHistoricalBidAsksAsync(ticker, new DateTime(2023, 04, 06, 10, 0, 0), new DateTime(2023, 04, 06, 11, 0, 0));

            //await _historicalProvider.GetHistoricalLastsAsync(ticker, new DateTime(2023, 04, 03));
            //await _historicalProvider.GetHistoricalLastsAsync(ticker, new DateTime(2023, 04, 04));
            //await _historicalProvider.GetHistoricalLastsAsync(ticker, new DateTime(2023, 04, 05));
            //await _historicalProvider.GetHistoricalLastsAsync(ticker, new DateTime(2023, 04, 06, 10, 0, 0), new DateTime(2023, 04, 06, 11, 0, 0));
            //DeleteData(ticker, new DateTime(2023, 04, 07));
            await Task.CompletedTask;
        }

        [Test]
        public async Task GetHistoricalOneSecBars_Holiday_ShouldThrow()
        {
            string ticker = "GME";
            DateTime date = new DateTime(2023, 04, 07);

            Assert.ThrowsAsync<ArgumentException>(async () => await _historicalProvider.GetHistoricalOneSecBarsAsync(ticker, date));
            await Task.CompletedTask;
        }

        [Test]
        public async Task GetHistoricalOneSecBars_SpecificDate()
        {
            string ticker = "GME";
            DateTime date = new DateTime(2023, 04, 03);

            var results = await _historicalProvider.GetHistoricalOneSecBarsAsync(ticker, date);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.AreEqual(NbOfOneSecBarsInADay, results.Count());
        }

        [Test]
        public async Task GetHistoricalOneSecBars_DateRange_IntraDay()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 10, 0, 0);
            DateTime to = new DateTime(2023, 04, 03, 11, 0, 0);

            var results = await _historicalProvider.GetHistoricalOneSecBarsAsync(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.AreEqual(3600, results.Count());
        }

        [Test]
        public async Task GetHistoricalOneSecBars_DateRange_MultipleDays()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 12, 0, 0);
            DateTime to = new DateTime(2023, 04, 05, 10, 30, 0);

            var results = await _historicalProvider.GetHistoricalOneSecBarsAsync(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.AreEqual(4*3600 + NbOfOneSecBarsInADay + 3600, results.Count());
        }

        [Test]
        public async Task GetHistoricalOneSecBars_DbDisabled_RetrievesThemFromIBKR()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 10, 0, 0);
            DateTime to = new DateTime(2023, 04, 03, 11, 0, 0);
            _historicalProvider.EnableDb = false;

            var results = await _historicalProvider.GetHistoricalOneSecBarsAsync(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.AreEqual(3600, results.Count());
            Assert.AreEqual(3600, _historicalProvider._nbRetrievedFromIBKR);
        }

        [Test]
        public async Task GetHistoricalOneSecBars_DbEnabled_RetrievesThemFromDb()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 10, 0, 0);
            DateTime to = new DateTime(2023, 04, 03, 11, 0, 0);

            var results = await _historicalProvider.GetHistoricalOneSecBarsAsync(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.AreEqual(3600, results.Count());
            Assert.AreEqual(3600, _historicalProvider._nbRetrievedFromDb);
        }

        [Test]
        public async Task GetHistoricalOneSecBars_DbEnabled_DataNotInDb_InsertsIt()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 06, 13, 0, 0);
            DateTime to = new DateTime(2023, 04, 06, 14, 0, 0);
            DeleteData(ticker, from, to);

            var results = await _historicalProvider.GetHistoricalOneSecBarsAsync(ticker, from, to);

            try
            {
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.AreEqual(3600, results.Count());
                Assert.AreEqual(3600, _historicalProvider._nbInsertedInDb);
            }
            finally
            {
                DeleteData(ticker, from, to);
            }
        }

        [Test]
        public async Task GetHistoricalOneSecBars_DbDisabled_DataNotInDb_DoesntInsertsIt()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 06, 13, 0, 0);
            DateTime to = new DateTime(2023, 04, 06, 14, 0, 0);
            DeleteData(ticker, from, to);
            _historicalProvider.EnableDb = false;

            var results = await _historicalProvider.GetHistoricalOneSecBarsAsync(ticker, from, to);

            try
            {
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.AreEqual(3600, results.Count());
                Assert.AreEqual(0, _historicalProvider._nbInsertedInDb);

            }
            finally
            {
                DeleteData(ticker, from, to);
            }
        }

        [Test]
        public async Task GetHistoricalBidAsks_Holiday_ShouldThrow()
        {
            string ticker = "GME";
            DateTime date = new DateTime(2023, 04, 07);

            Assert.ThrowsAsync<ArgumentException>(async () => await _historicalProvider.GetHistoricalBidAsksAsync(ticker, date));
            await Task.CompletedTask;
        }

        [Test]
        public async Task GetHistoricalBidAsks_SpecificDate()
        {
            string ticker = "GME";
            DateTime date = new DateTime(2023, 04, 03);

            var results = await _historicalProvider.GetHistoricalBidAsksAsync(ticker, date);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
        }

        [Test]
        public async Task GetHistoricalBidAsks_DateRange_IntraDay()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 10, 0, 0);
            DateTime to = new DateTime(2023, 04, 03, 11, 0, 0);

            var results = await _historicalProvider.GetHistoricalBidAsksAsync(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
        }

        [Test]
        public async Task GetHistoricalBidAsks_DateRange_MultipleDays()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 12, 0, 0);
            DateTime to = new DateTime(2023, 04, 05, 10, 30, 0);

            var results = await _historicalProvider.GetHistoricalBidAsksAsync(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
        }

        [Test]
        public async Task GetHistoricalBidAsks_DbDisabled_RetrievesThemFromIBKR()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 10, 0, 0);
            DateTime to = new DateTime(2023, 04, 03, 11, 0, 0);
            _historicalProvider.EnableDb = false;

            var results = await _historicalProvider.GetHistoricalBidAsksAsync(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.Greater(_historicalProvider._nbRetrievedFromIBKR, 0);
        }

        [Test]
        public async Task GetHistoricalBidAsks_DbEnabled_RetrievesThemFromDb()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 10, 0, 0);
            DateTime to = new DateTime(2023, 04, 03, 11, 0, 0);

            var results = await _historicalProvider.GetHistoricalBidAsksAsync(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.Greater(_historicalProvider._nbRetrievedFromDb, 0);
        }

        [Test]
        public async Task GetHistoricalBidAsks_DbEnabled_DataNotInDb_InsertsIt()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 06, 13, 0, 0);
            DateTime to = new DateTime(2023, 04, 06, 14, 0, 0);
            DeleteData(ticker, from, to);

            var results = await _historicalProvider.GetHistoricalBidAsksAsync(ticker, from, to);

            try
            {
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.Greater(_historicalProvider._nbInsertedInDb, 0);
            }
            finally
            {
                DeleteData(ticker, from, to);
            }
        }

        [Test]
        public async Task GetHistoricalBidAsks_DbDisabled_DataNotInDb_DoesntInsertsIt()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 06, 13, 0, 0);
            DateTime to = new DateTime(2023, 04, 06, 14, 0, 0);
            DeleteData(ticker, from, to);
            _historicalProvider.EnableDb = false;

            var results = await _historicalProvider.GetHistoricalBidAsksAsync(ticker, from, to);

            try
            {
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.AreEqual(0, _historicalProvider._nbInsertedInDb);

            }
            finally
            {
                DeleteData(ticker, from, to);
            }
        }

        [Test]
        public async Task GetHistoricalLasts_Holiday_ShouldThrow()
        {
            string ticker = "GME";
            DateTime date = new DateTime(2023, 04, 07);

            Assert.ThrowsAsync<ArgumentException>(async () => await _historicalProvider.GetHistoricalLastsAsync(ticker, date));
            await Task.CompletedTask;
        }

        [Test]
        public async Task GetHistoricalLasts_SpecificDate()
        {
            string ticker = "GME";
            DateTime date = new DateTime(2023, 04, 03);

            var results = await _historicalProvider.GetHistoricalLastsAsync(ticker, date);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
        }

        [Test]
        public async Task GetHistoricalLasts_DateRange_IntraDay()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 10, 0, 0);
            DateTime to = new DateTime(2023, 04, 03, 11, 0, 0);

            var results = await _historicalProvider.GetHistoricalLastsAsync(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
        }

        [Test]
        public async Task GetHistoricalLasts_DateRange_MultipleDays()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 12, 0, 0);
            DateTime to = new DateTime(2023, 04, 05, 10, 30, 0);

            var results = await _historicalProvider.GetHistoricalLastsAsync(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
        }

        [Test]
        public async Task GetHistoricalLasts_DbDisabled_RetrievesThemFromIBKR()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 10, 0, 0);
            DateTime to = new DateTime(2023, 04, 03, 11, 0, 0);
            _historicalProvider.EnableDb = false;

            var results = await _historicalProvider.GetHistoricalLastsAsync(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.Greater(_historicalProvider._nbRetrievedFromIBKR, 0);
            Assert.AreEqual(0, _historicalProvider._nbRetrievedFromDb);
        }

        [Test]
        public async Task GetHistoricalLasts_DbEnabled_RetrievesThemFromDb()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 03, 10, 0, 0);
            DateTime to = new DateTime(2023, 04, 03, 11, 0, 0);

            var results = await _historicalProvider.GetHistoricalLastsAsync(ticker, from, to);

            Assert.IsNotNull(results);
            Assert.IsNotEmpty(results);
            Assert.Greater(_historicalProvider._nbRetrievedFromDb, 0);
            Assert.AreEqual(0, _historicalProvider._nbRetrievedFromIBKR);
        }

        [Test]
        public async Task GetHistoricalLasts_DbEnabled_DataNotInDb_InsertsIt()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 06, 13, 0, 0);
            DateTime to = new DateTime(2023, 04, 06, 14, 0, 0);
            DeleteData(ticker, from, to);

            var results = await _historicalProvider.GetHistoricalLastsAsync(ticker, from, to);

            try
            {
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.Greater(_historicalProvider._nbInsertedInDb, 0);
            }
            finally
            {
                DeleteData(ticker, from, to);
            }
        }

        [Test]
        public async Task GetHistoricalLasts_DbDisabled_DataNotInDb_DoesntInsertsIt()
        {
            string ticker = "GME";
            DateTime from = new DateTime(2023, 04, 06, 13, 0, 0);
            DateTime to = new DateTime(2023, 04, 06, 14, 0, 0);
            DeleteData(ticker, from, to);
            _historicalProvider.EnableDb = false;

            var results = await _historicalProvider.GetHistoricalLastsAsync(ticker, from, to);

            try
            {
                Assert.IsNotNull(results);
                Assert.IsNotEmpty(results);
                Assert.AreEqual(0, _historicalProvider._nbInsertedInDb);

            }
            finally
            {
                DeleteData(ticker, from, to);
            }
        }

        void DeleteData(string ticker, DateTime date)
        {
            var d = date.ToMarketHours();
            DeleteData(ticker, d.Item1, d.Item2);
        }

        void DeleteData(string ticker, DateTime from, DateTime to) 
        {
            Debug.Assert(_historicalProvider.DbPath == TestDbPath);

            var connection = new SqliteConnection("Data Source=" + TestDbPath);
            connection.Open();

            string[] tableParts = new string[3]
            {
                "Bar", "BidAsk", "Last"
            };

            foreach (var tablePart in tableParts)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText =
                $@"
                    DELETE FROM Historical{tablePart}
                    WHERE Stock = (
                       SELECT Stock.Id from Stock
                       WHERE Stock.Symbol = '{ticker}'
                       AND DateTime >= {new DateTimeOffset(from).ToUnixTimeSeconds()} 
                       AND DateTime < {new DateTimeOffset(to).ToUnixTimeSeconds()} 
                    )
                ";
                cmd.ExecuteNonQuery();
            }

            connection.Close();
        }
    }
}
