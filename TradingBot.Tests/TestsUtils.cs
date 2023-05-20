using System.Data;
using Microsoft.Data.Sqlite;
using NLog;
using NLog.TradingBot;
using NUnit.Framework;
using TradingBot.Backtesting;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;
using TradingBot.IBKR;
using TradingBot.Utils;

namespace TradingBot.Tests
{
    internal static class TestsUtils
    {
        public const string TestDbPath = @"C:\tradingbot\db\tests.sqlite3";
        public const string TestDataDbPath = @"C:\tradingbot\db\testsdata.sqlite3";
        
        public static void PrintCurrentTestName(this ILogger logger)
        {
            logger?.Info($"=== CURRENT TEST : {TestContext.CurrentContext.Test.Name}");
        }

        public static ILogger CreateLogger()
        {
            return LogManager.GetLogger($"NUnitLogger", typeof(NunitTargetLogger));
        }

        public static IBBroker CreateBroker(ILogger? logger = null)
        {
            var broker = new IBBroker(9001, logger);
            var historicalProvider = (IBHistoricalDataProvider)broker.HistoricalDataProvider;
            historicalProvider.DbPath = TestDbPath;

            //historical data provider always has a logger
            historicalProvider.Logger = logger ?? CreateLogger();
            return broker;
        }

        public static Backtester CreateBacktester(DateOnly date)
        {
            var backtester = new Backtester(date);
            backtester.DbPath = TestDbPath;
            backtester.Logger = CreateLogger();

            var historicalProvider = (IBHistoricalDataProvider)backtester.HistoricalDataProvider;
            historicalProvider.DbPath = TestDbPath;

            //historical data provider always has a logger
            historicalProvider.Logger = CreateLogger();

            return backtester;
        }

        public static Backtester CreateBacktester(DateTime from, DateTime to)
        {
            var backtester = new Backtester(from, to);
            backtester.DbPath = TestDbPath;
            backtester.Logger = CreateLogger();
            return backtester;
        }

        public static bool IsMarketOpen()
        {
            if (TestContext.CurrentContext.Test.FullName.Contains("BacktesterTests")) return true;
            else return MarketDataUtils.IsMarketOpen();
        }

        public static class Assert
        {
            public static void MarketIsOpen()
            {
                if (!IsMarketOpen())
                    NUnit.Framework.Assert.Inconclusive("Market is not open.");
            }
        }

        internal static void DeleteDataInTestDb<TData>(string ticker, DateRange dateRange, SqliteConnection sqliteConnection = null) where TData : IMarketData, new()
        {
            var connection = sqliteConnection;
            if (sqliteConnection == null)
            {
                connection = new SqliteConnection("Data Source=" + TestDbPath);
                connection.Open();
            }

            NUnit.Framework.Assert.AreEqual(TestDbPath, connection.DataSource);

            var cmd = connection.CreateCommand();
            cmd.CommandText =
            $@"
                DELETE FROM {typeof(TData).Name}s
                WHERE Ticker = (
                    SELECT Tickers.Id from Tickers
                    WHERE Tickers.Symbol = '{ticker}'
                    AND DateTime >= {dateRange.From.ToUnixTimeSeconds()} 
                    AND DateTime < {dateRange.To.ToUnixTimeSeconds()} 
                )
            ";
            cmd.ExecuteNonQuery();

            if(sqliteConnection == null)
                connection.Close();
        }

        internal static void RestoreTestDb(SqliteConnection sqliteConnection = null)
        {
            var connection = sqliteConnection;
            if (sqliteConnection == null)
            {
                connection = new SqliteConnection("Data Source=" + TestDbPath);
                connection.Open();
            }

            NUnit.Framework.Assert.AreEqual(TestDbPath, connection.DataSource);

            var cmd = connection.CreateCommand();
            cmd.CommandText =$"SELECT name FROM sqlite_schema WHERE type='table' ORDER BY name";

            List<string> tableNames;
            using (SqliteDataReader reader = cmd.ExecuteReader())
                tableNames = reader.Cast<IDataRecord>().Select(dr => dr.GetString(0)).Where(s => s != "sqlite_sequence").ToList();

            cmd.CommandText = "PRAGMA ignore_check_constraints = 1; PRAGMA foreign_keys = 0;";
            foreach (var table in tableNames)
                cmd.CommandText += $"DELETE FROM {table};\n";
            cmd.ExecuteNonQuery();

            cmd.CommandText = $"ATTACH '{TestDataDbPath}' AS TestDataDb";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "";
            foreach (var table in tableNames)
                cmd.CommandText += $"INSERT INTO {table} SELECT * FROM TestDataDb.{table};\n";
            cmd.CommandText += "PRAGMA ignore_check_constraints = 0; PRAGMA foreign_keys = 1;";
            cmd.ExecuteNonQuery();

            cmd.CommandText = $"DETACH DATABASE 'TestDataDb'";
            cmd.ExecuteNonQuery();

            if (sqliteConnection == null)
                connection.Close();
        }
    }
}
