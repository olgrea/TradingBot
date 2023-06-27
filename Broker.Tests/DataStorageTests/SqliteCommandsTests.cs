using System.Data;
using Broker.DataStorage.Sqlite.DbCommandFactories;
using Broker.IBKR;
using Broker.MarketData;
using Broker.Utils;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Broker.Tests;

namespace SqliteCommandsTests
{
    internal class BarCommandsTests : SqliteCommandsTests<Bar> {}
    internal class BidAskCommandsTests : SqliteCommandsTests<BidAsk> {}
    internal class LastCommandsTests : SqliteCommandsTests<Last> {}

    abstract class SqliteCommandsTests<TData> where TData : IMarketData, new()
    {
        const string Ticker = "GME";
        protected DbCommandFactory _commandFactory;
        IBHistoricalDataProvider _historicalProvider;
        DateRange _dateRange = new DateRange(new DateTime(2023, 04, 06, 10, 30, 0), new DateTime(2023, 04, 06, 11, 00, 00));

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            _commandFactory = DbCommandFactory.Create<TData>(TestsUtils.TestDbPath);
            var broker = TestsUtils.CreateBroker();
            _historicalProvider = broker.HistoricalDataProvider as IBHistoricalDataProvider;
            Assert.NotNull(_historicalProvider);
        }

        [SetUp]
        public void Setup()
        {
            TestsUtils.RestoreTestDb(_commandFactory.Connection);
        }

        [Test]
        public void ExistsCommand_WhenCompleteRangeIsInDb_ReturnsTrue()
        {
            var cmd = _commandFactory.CreateExistsCommand(Ticker, _dateRange);
            Assert.IsTrue(cmd.Execute());
        }

        [Test]
        public void ExistsCommand_WhenMissingTimespansInDb_ReturnsFalse()
        {
            var toDelete = new DateRange(_dateRange.From, _dateRange.From.AddMinutes(5));
            TestsUtils.DeleteDataInTestDb<TData>(Ticker, toDelete, _commandFactory.Connection);

            var cmd = _commandFactory.CreateExistsCommand(Ticker, _dateRange);
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
            var range = new DateRange(_dateRange.From, _dateRange.From.AddMinutes(5));
            UpdateToNull(Ticker, range);
            var cmd = _commandFactory.CreateSelectCommand(Ticker, _dateRange);
            var results = cmd.Execute();
            Assert.NotNull(results);
            Assert.IsTrue(results.All(
                r => r.Time >= _dateRange.From.AddMinutes(5) 
                && r.Time < _dateRange.To)
            );
        }

        [Test]
        public void InsertCommand_WhenTimestanmpsAreMissing_InsertsNullValues()
        {
            var cmd = _commandFactory.CreateSelectCommand(Ticker, _dateRange);
            var data = cmd.Execute().SkipWhile(d => d.Time < _dateRange.From.AddMinutes(5));

            TestsUtils.DeleteDataInTestDb<TData>(Ticker, _dateRange, _commandFactory.Connection);
            
            var insertCmd = _commandFactory.CreateInsertCommand(Ticker, _dateRange, data);
            var nbInserted = insertCmd.Execute();
            Assert.Greater(nbInserted, 0);

            IEnumerable<IMarketData> rowsWithNullValues = SelectNulls(Ticker, new(_dateRange.From, _dateRange.From.AddMinutes(5)));
            Assert.IsNotEmpty(rowsWithNullValues);
            Assert.IsTrue(rowsWithNullValues.All(d => d.Time < _dateRange.From.AddMinutes(5)));

            if (typeof(TData) == typeof(Bar))
            {
                Assert.IsTrue(rowsWithNullValues.Cast<Bar>().All(d => double.IsNaN(d.Open)));
            }
            else if (typeof(TData) == typeof(BidAsk))
            {
                Assert.IsTrue(rowsWithNullValues.Cast<BidAsk>().All(d => double.IsNaN(d.Bid)));
            }
            else if (typeof(TData) == typeof(Last))
            {
                Assert.IsTrue(rowsWithNullValues.Cast<Last>().All(d => double.IsNaN(d.Price)));
            }
        }

        void UpdateToNull(string ticker, DateRange dateRange)
        {
            var cmd = _commandFactory.Connection.CreateCommand();

            if (typeof(TData) == typeof(Bar))
            {
                cmd.CommandText =
                $@"
                    UPDATE Bars
                    SET OHLC = NULL
                    WHERE Ticker = (SELECT Id FROM Tickers WHERE Tickers.Symbol = '{ticker}')
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
                    WHERE Ticker = (SELECT Id FROM Tickers WHERE Tickers.Symbol = '{ticker}')
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
                    WHERE Ticker = (SELECT Id FROM Tickers WHERE Tickers.Symbol = '{ticker}')
                    AND DateTime >= {dateRange.From.ToUnixTimeSeconds()} 
                    AND DateTime < {dateRange.To.ToUnixTimeSeconds()} 
                ";
            }

            cmd.ExecuteNonQuery();
        }

        IEnumerable<IMarketData> SelectNulls(string ticker, DateRange dateRange)
        {
            var cmd = _commandFactory.Connection.CreateCommand();

            if(typeof(TData) == typeof(Bar))
            {
                cmd.CommandText =
                $@"
                    SELECT * FROM BarsView
                    WHERE Ticker = '{ticker}'
                    AND Date >= '{dateRange.From.ToShortDateString()}' 
                    AND Time >= '{dateRange.From.TimeOfDay}' 
                    AND Date <= '{dateRange.To.ToShortDateString()}' 
                    AND Time < '{dateRange.To.TimeOfDay}' 
                    AND Open IS NULL
                ";

                using SqliteDataReader reader = cmd.ExecuteReader();
                return new LinkedList<IMarketData>(reader.Cast<IDataRecord>().Select(dr => 
                {
                    DateTime dateTime = DateTime.Parse(dr.GetString(1));
                    dateTime = dateTime.AddTicks(TimeSpan.Parse(dr.GetString(2)).Ticks);
                    object val = dr.GetValue(4);
                    return new Bar() { Time = dateTime, Open = val is DBNull ? double.NaN : Convert.ToDouble(val)};
                }));
            }
            else if (typeof(TData) == typeof(BidAsk))
            {
                cmd.CommandText =
                $@"
                    SELECT * FROM BidAsksView
                    WHERE Ticker = '{ticker}'
                    AND Date >= '{dateRange.From.ToShortDateString()}' 
                    AND Time >= '{dateRange.From.TimeOfDay}' 
                    AND Date <= '{dateRange.To.ToShortDateString()}' 
                    AND Time < '{dateRange.To.TimeOfDay}' 
                    AND Bid IS NULL
                ";

                using SqliteDataReader reader = cmd.ExecuteReader();
                return new LinkedList<IMarketData>(reader.Cast<IDataRecord>().Select(dr =>
                {
                    DateTime dateTime = DateTime.Parse(dr.GetString(1));
                    dateTime = dateTime.AddTicks(TimeSpan.Parse(dr.GetString(2)).Ticks);
                    object val = dr.GetValue(3);
                    return new BidAsk() { Time = dateTime, Bid = val is DBNull ? double.NaN : Convert.ToDouble(val) };
                }));
            }
            else if (typeof(TData) == typeof(Last))
            {
                cmd.CommandText =
                $@"
                    SELECT * FROM LastsView
                    WHERE Ticker = '{ticker}'
                    AND Date >= '{dateRange.From.ToShortDateString()}' 
                    AND Time >= '{dateRange.From.TimeOfDay}' 
                    AND Date <= '{dateRange.To.ToShortDateString()}' 
                    AND Time < '{dateRange.To.TimeOfDay}' 
                    AND Price IS NULL
                ";

                using SqliteDataReader reader = cmd.ExecuteReader();
                return new LinkedList<IMarketData>(reader.Cast<IDataRecord>().Select(dr =>
                {
                    DateTime dateTime = DateTime.Parse(dr.GetString(1));
                    dateTime = dateTime.AddTicks(TimeSpan.Parse(dr.GetString(2)).Ticks);
                    object val = dr.GetValue(3);
                    return new Last() { Time = dateTime, Price = val is DBNull ? double.NaN : Convert.ToDouble(val) };
                }));
            }

            return null;
        }

        async Task FillTestDataDb()
        {
            var path = _historicalProvider.DbPath;
            try
            {
                _historicalProvider.DbPath = TestsUtils.TestDataDbPath;
                await _historicalProvider.GetHistoricalDataAsync<TData>(Ticker, _dateRange.From, _dateRange.To);
            }
            finally
            {
                _historicalProvider.DbPath = path;
            }
        }
    }
}
