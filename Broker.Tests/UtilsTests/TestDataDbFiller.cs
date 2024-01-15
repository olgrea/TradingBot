using System.Data;
using BacktesterTests;
using Broker.IBKR;
using Broker.MarketData;
using Broker.Tests;
using Broker.Utils;
using IBBrokerTests;
using Microsoft.Data.Sqlite;
using NLog;
using NUnit.Framework;
using NUnit.Framework.Internal;
using SqliteCommandsTests;

namespace UtilsTests
{
    internal class TestDataDbFiller
    {
        static List<(string, DateRange)> GetRequiredTestData()
        {
            List<(string, DateRange)> data = new();
            data.AddRange(BacktesterLiveDataProviderTests.GetRequiredTestData());
            data.AddRange(BacktesterOrderManagerTests.GetRequiredTestData());
            data.AddRange(OrderEvaluatorTests.GetRequiredTestData());
            data.AddRange(BarCommandsTests.GetRequiredTestData());
            data.AddRange(HistoricalOneSecBarsTests.GetRequiredTestData());
            return data;
        }

        [Test, Explicit]
        public async Task FillTestDataDb()
        {
            var logger = TestsUtils.CreateLogger();
            var broker = TestsUtils.CreateBroker(logger);
            var hdp = (IBHistoricalDataProvider)broker.HistoricalDataProvider;
            hdp.DbPath = TestsUtils.TestDataDbPath;
            hdp.LogLevel = LogLevel.Debug;

            List<(string, DateRange)> data = GetRequiredTestData();

            foreach (var d in data.Distinct())
            {
                await hdp.GetHistoricalDataAsync<Bar>(d.Item1, d.Item2.From, d.Item2.To);
                await hdp.GetHistoricalDataAsync<BidAsk>(d.Item1, d.Item2.From, d.Item2.To);
                await hdp.GetHistoricalDataAsync<Last>(d.Item1, d.Item2.From, d.Item2.To);
            }

            await broker.DisconnectAsync();
        }

        //[Test, Explicit]
        public void ClearTestDataDb()
        {
            Assert.IsTrue(TestsUtils.TestDataDbPath.Contains("testsdata.sqlite3"));

            var connection = new SqliteConnection("Data Source=" + TestsUtils.TestDataDbPath);
            connection.Open();
            var cmd = connection.CreateCommand();

            cmd.CommandText = "PRAGMA ignore_check_constraints = 1; PRAGMA foreign_keys = 0;";
            cmd.ExecuteNonQuery();
            
            List<string> tables = null;
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            using (SqliteDataReader reader = cmd.ExecuteReader())
            {
                tables = reader.Cast<IDataRecord>().Select(dr => dr.GetString(0)).ToList();
            }

            foreach (var table in tables)
            {
                cmd.CommandText = $"DELETE FROM {table}";
                cmd.ExecuteNonQuery();
            }

            cmd.CommandText = "PRAGMA ignore_check_constraints = 0; PRAGMA foreign_keys = 1;";
            cmd.ExecuteNonQuery();

            connection.Close();
        }
    }
}
