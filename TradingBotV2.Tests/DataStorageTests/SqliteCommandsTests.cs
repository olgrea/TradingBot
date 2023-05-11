using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.DataStorage.Sqlite.DbCommandFactories;
using TradingBotV2.Broker.MarketData.Providers;
using TradingBotV2.Tests;
using TradingBotV2.Utils;

namespace SqliteCommandsTests
{
    internal class BarCommandsTests : SqliteCommandsTests<Bar> {}
    internal class BidAskCommandsTests : SqliteCommandsTests<BidAsk> {}
    internal class LastCommandsTests : SqliteCommandsTests<Last> {}

    abstract class SqliteCommandsTests<TData> where TData : IMarketData, new()
    {
        const string Ticker = "GME";
        protected DbCommandFactory _commandFactory;
        IHistoricalDataProvider _historicalProvider;
        DateTime From = new DateTime(2023, 04, 06, 10, 00, 0);
        DateTime To = new DateTime(2023, 04, 06, 11, 00, 00);

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            _commandFactory = DbCommandFactory.Create<TData>(TestsUtils.TestDbPath);
            var broker = TestsUtils.CreateBroker();
            _historicalProvider = broker.HistoricalDataProvider;
        }

        [SetUp]
        public async Task Setup()
        {
            await _historicalProvider.GetHistoricalDataAsync<TData>(Ticker, From, To);
        }

        [Test]
        public void ExistsCommand_WhenCompleteRangeIsInDb_ReturnsTrue()
        {
            var cmd = _commandFactory.CreateExistsCommand(Ticker, From.Date, (From.TimeOfDay, To.TimeOfDay));
            Assert.IsTrue(cmd.Execute());
        }

        [Test]
        public void ExistsCommand_WhenTimespansAreMissingInRange_ReturnsFalse()
        {
            DeleteData(Ticker, From.AddMinutes(5), To.AddMinutes(-5));
            var cmd = _commandFactory.CreateExistsCommand(Ticker, From.Date, (From.TimeOfDay, To.TimeOfDay));
            Assert.IsFalse(cmd.Execute());
        }

        void DeleteData(string ticker, DateTime from, DateTime to)
        {
            Assert.AreEqual(_commandFactory.DbPath, TestsUtils.TestDbPath);

            var connection = new SqliteConnection("Data Source=" + TestsUtils.TestDbPath);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText =
            $@"
                DELETE FROM Historical{typeof(TData).Name}
                WHERE Stock = (
                    SELECT Stock.Id from Stock
                    WHERE Stock.Symbol = '{ticker}'
                    AND DateTime >= {from.ToUnixTimeSeconds()} 
                    AND DateTime < {to.ToUnixTimeSeconds()} 
                )
            ";
            cmd.ExecuteNonQuery();

            if (typeof(TData) == typeof(Bar))
                return;

            cmd.CommandText =
            $@"
                DELETE FROM {typeof(TData).Name}TimeStamps
                WHERE Stock = (
                    SELECT Stock.Id from Stock
                    WHERE Stock.Symbol = '{ticker}'
                    AND DateTime >= {from.ToUnixTimeSeconds()} 
                    AND DateTime < {to.ToUnixTimeSeconds()} 
                )
            ";
            cmd.ExecuteNonQuery();

            connection.Close();
        }
    }
}
