using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.DataStorage.Sqlite.DbCommandFactories;
using TradingBotV2.Broker.MarketData.Providers;
using TradingBotV2.Tests;
using TradingBotV2.Utils;
using System.Data;

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
        DateRange _dateRange = new DateRange(new DateTime(2023, 04, 06, 10, 30, 0), new DateTime(2023, 04, 06, 11, 00, 00));
        IEnumerable<IMarketData> _marketData;

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
            _marketData = await _historicalProvider.GetHistoricalDataAsync<TData>(Ticker, _dateRange.From, _dateRange.To);
        }

        [Test]
        public void ExistsCommand_WhenCompleteRangeIsInDb_ReturnsTrue()
        {
            var cmd = _commandFactory.CreateExistsCommand(Ticker, _dateRange);
            Assert.IsTrue(cmd.Execute());
        }

        [Test]
        public void ExistsCommand_WhenTimespansAreMissingInRange_ReturnsFalse()
        {
            var range = new DateRange(_dateRange.From.AddMinutes(5), _dateRange.To.AddMinutes(-5));
            var cmd = _commandFactory.CreateExistsCommand(Ticker, range);
            Assert.IsFalse(cmd.Execute());
        }

        [Test]
        public void SelectCommand_ReturnsRowsAssociatedWithTimeRange()
        {
            var cmd = _commandFactory.CreateSelectCommand(Ticker, _dateRange);
            var results = cmd.Execute();
            Assert.NotNull(results);
            Assert.IsTrue(results.All(r => r.Time >= _dateRange.From && r.Time < _dateRange.To));
        }

        [Test]
        public void SelectCommand_WhenSomeRowsHasNullValues_SkipsThem()
        {
            var range = new DateRange(_dateRange.From.AddMinutes(5), _dateRange.To.AddMinutes(-5));
            UpdateToNull(Ticker, range);
            var cmd = _commandFactory.CreateSelectCommand(Ticker, range);
            var results = cmd.Execute();
            Assert.NotNull(results);
            Assert.IsTrue(results.All(
                r => r.Time >= _dateRange.From 
                && r.Time < _dateRange.From.AddMinutes(5) 
                && r.Time >= _dateRange.To.AddMinutes(-5) 
                && r.Time < _dateRange.To)
            );
        }

        [Test]
        public void InsertCommand_WhenTimestanmpsAreMissing_InsertsNullValues()
        {
            TestsUtils.DeleteDataInTestDb<TData>(Ticker, _dateRange);
            var data = _marketData.SkipWhile(d => d.Time < _dateRange.From.AddMinutes(5));
            
            var insertCmd = _commandFactory.CreateInsertCommand(Ticker, _dateRange, data);
            var nbInserted = insertCmd.Execute();
            Assert.Greater(nbInserted, 0);

            IEnumerable<IMarketData> rowsWithNullValues = SelectNulls(Ticker, new(_dateRange.From, _dateRange.From.AddMinutes(5)));
            Assert.IsNotEmpty(rowsWithNullValues);
            Assert.IsTrue(rowsWithNullValues.All(d => d.Time < _dateRange.From.AddMinutes(5)));

            if (typeof(TData) == typeof(Bar))
            {
                Assert.IsTrue(rowsWithNullValues.Cast<Bar>().All(d => d.Open == -1));
            }
            else if (typeof(TData) == typeof(Last))
            {
                Assert.IsTrue(rowsWithNullValues.Cast<BidAsk>().All(d => d.Bid == -1));
            }
            else if (typeof(TData) == typeof(BidAsk))
            {
                Assert.IsTrue(rowsWithNullValues.Cast<Last>().All(d => d.Price == -1));
            }
        }

        void UpdateToNull(string ticker, DateRange dateRange)
        {
            var connection = new SqliteConnection("Data Source=" + TestsUtils.TestDbPath);
            connection.Open();

            var cmd = connection.CreateCommand();

            if (typeof(TData) == typeof(Bar))
            {
                cmd.CommandText =
                $@"
                    UPDATE Bars
                    SET OHLC = NULL
                    WHERE Ticker = '{ticker}'
                    AND DateTime >= {dateRange.From.ToUnixTimeSeconds()}
                    AND DateTime < {dateRange.To.ToUnixTimeSeconds()}
                ";
            }
            else if (typeof(TData) == typeof(BidAsk))
            {
                cmd.CommandText =
                $@"
                    UPDATE BidAsks
                    SET BidAsk = NULL
                    WHERE Ticker = '{ticker}'
                    AND DateTime >= {dateRange.From.ToUnixTimeSeconds()} 
                    AND DateTime < {dateRange.To.ToUnixTimeSeconds()}
                ";
            }
            else if (typeof(TData) == typeof(Last))
            {
                cmd.CommandText =
                $@"
                    UPDATE Lasts
                    SET Price = NULL
                    WHERE Ticker = '{ticker}'
                    AND DateTime >= {dateRange.From.ToUnixTimeSeconds()} 
                    AND DateTime < {dateRange.To.ToUnixTimeSeconds()} 
                ";
            }

            cmd.ExecuteNonQuery();
        }

        IEnumerable<IMarketData> SelectNulls(string ticker, DateRange dateRange)
        {
            var connection = new SqliteConnection("Data Source=" + TestsUtils.TestDbPath);
            connection.Open();

            var cmd = connection.CreateCommand();

            if(typeof(TData) == typeof(Bar))
            {
                cmd.CommandText =
                $@"
                    SELECT * FROM BarsView
                    WHERE Ticker = '{ticker}'
                    AND Date >= '{dateRange.From.ToShortDateString()}' 
                    AND Time >= '{dateRange.From.ToShortTimeString()}' 
                    AND Date <= '{dateRange.To.ToShortDateString()}' 
                    AND Time < '{dateRange.From.ToShortTimeString()}' 
                    AND Open IS NULL
                ";

                using SqliteDataReader reader = cmd.ExecuteReader();
                return new LinkedList<IMarketData>(reader.Cast<IDataRecord>().Select(dr => 
                {
                    DateTime dateTime = DateTime.Parse(dr.GetString(1));
                    dateTime = dateTime.AddTicks(TimeSpan.Parse(dr.GetString(2)).Ticks);
                    object val = dr.GetValue(4);
                    return new Bar() { Time = dateTime, Open = Convert.ToDouble(val ?? -1)};
                }));
            }
            else if (typeof(TData) == typeof(BidAsk))
            {
                cmd.CommandText =
                $@"
                    SELECT FROM BidAsksView
                    WHERE Ticker = (
                        SELECT Tickers.Id FROM Tickers
                        WHERE Tickers.Symbol = '{ticker}'
                        AND Date >= '{dateRange.From.ToShortDateString()}' 
                        AND Time >= '{dateRange.From.ToShortTimeString()}' 
                        AND Date <= '{dateRange.To.ToShortDateString()}' 
                        AND Time < '{dateRange.From.ToShortTimeString()}' 
                        AND Bid IS NULL
                    )
                ";

                using SqliteDataReader reader = cmd.ExecuteReader();
                return new LinkedList<IMarketData>(reader.Cast<IDataRecord>().Select(dr =>
                {
                    DateTime dateTime = DateTime.Parse(dr.GetString(1));
                    dateTime = dateTime.AddTicks(TimeSpan.Parse(dr.GetString(2)).Ticks);
                    object val = dr.GetValue(3);
                    return new BidAsk() { Time = dateTime, Bid = Convert.ToDouble(val ?? -1) };
                }));
            }
            else if (typeof(TData) == typeof(Last))
            {
                cmd.CommandText =
                $@"
                    SELECT FROM Lasts
                    WHERE Ticker = (
                        SELECT Tickers.Id FROM Tickers
                        WHERE Tickers.Symbol = '{ticker}'
                        AND Date >= '{dateRange.From.ToShortDateString()}' 
                        AND Time >= '{dateRange.From.ToShortTimeString()}' 
                        AND Date < '{dateRange.To.ToShortDateString()}' 
                        AND Time < '{dateRange.From.ToShortTimeString()}' 
                        AND Price IS NULL
                    )
                ";

                using SqliteDataReader reader = cmd.ExecuteReader();
                return new LinkedList<IMarketData>(reader.Cast<IDataRecord>().Select(dr =>
                {
                    DateTime dateTime = DateTime.Parse(dr.GetString(1));
                    dateTime = dateTime.AddTicks(TimeSpan.Parse(dr.GetString(2)).Ticks);
                    object val = dr.GetValue(3);
                    return new Last() { Time = dateTime, Price = Convert.ToDouble(val ?? -1) };
                }));
            }

            return null;
        }
    }
}
