using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Utils;

namespace TradingBotV2.DataStorage.Sqlite.DbCommands
{
    public abstract class DbCommand<TResult>
    {
        protected SqliteConnection _connection;

        protected DbCommand(SqliteConnection connection)
        {
            _connection = connection;
        }

        public abstract TResult Execute();

        protected string Sanitize(object value)
        {
            if (value is string)
            {
                return $"'{value}'";
            }
            else if (value is DateTime dt)
            {
                return $"'{dt.ToShortDateString()}'";
            }
            else if (value is TimeSpan ts)
            {
                return $"'{ts}'";
            }
            else if (value is BarLength bl)
            {
                return ((int)bl).ToString();
            }
            else if (value is double d)
            {
                return d.ToString(CultureInfo.InvariantCulture);
            }
            else
                return value?.ToString() ?? string.Empty;
        }
    }

    internal abstract class ExistsCommand<TMarketData> : DbCommand<bool>
    {
        protected string _symbol;
        protected DateTime _date;
        protected (TimeSpan, TimeSpan) _timeRange;
        protected string _marketDataName;

        public ExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange, SqliteConnection connection) : base(connection)
        {
            _symbol = symbol;
            _date = date;
            _timeRange = timeRange;
            _marketDataName = typeof(TMarketData).Name;
        }

        public override bool Execute()
        {
            SqliteCommand command = _connection.CreateCommand();
            command.CommandText = MakeExistsCommandText();
            using SqliteDataReader reader = command.ExecuteReader();
            var v = reader.Cast<IDataRecord>().First().GetInt64(0);
            return Convert.ToBoolean(v);
        }

        protected virtual string MakeExistsCommandText()
        {
            var nbTimestamps = (_timeRange.Item2 - _timeRange.Item1).TotalSeconds;
            return
            $@"
                SELECT EXISTS (
                    SELECT 1 WHERE 
                        (SELECT COUNT(DISTINCT DateTime) FROM {_marketDataName}Timestamps
                            LEFT JOIN Stock ON Stock.Id = {_marketDataName}Timestamps.Stock
                            WHERE Symbol = {Sanitize(_symbol)}
                            AND DateTime >= {Sanitize(new DateTime(_date.Date.Ticks + _timeRange.Item1.Ticks).ToUnixTimeSeconds())} 
                            AND DateTime < {Sanitize(new DateTime(_date.Date.Ticks + _timeRange.Item2.Ticks).ToUnixTimeSeconds())} 
                        ) = {Sanitize(nbTimestamps)}
                );
            ";
        }
    }

    internal abstract class SelectCommand<TMarketData> : DbCommand<IEnumerable<IMarketData>>
    {
        protected string _symbol;
        protected DateTime _date;
        protected (TimeSpan, TimeSpan) _timeRange;
        protected string _marketDataName;

        public SelectCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange, SqliteConnection connection) : base(connection)
        {
            _symbol = symbol;
            _date = date;
            _timeRange = timeRange;
            _marketDataName = typeof(TMarketData).Name;
        }

        public override IEnumerable<IMarketData> Execute()
        {
            SqliteCommand command = _connection.CreateCommand();
            command.CommandText = MakeSelectCommandText();
            using SqliteDataReader reader = command.ExecuteReader();
            return new LinkedList<IMarketData>(reader.Cast<IDataRecord>().Select(MakeDataFromResults));
        }

        protected virtual string MakeSelectCommandText()
        {
            return
            $@"
                SELECT * FROM Historical{_marketDataName}View
                WHERE Ticker = {Sanitize(_symbol)}
                AND Date = {Sanitize(_date)}
                AND Time >= {Sanitize(_timeRange.Item1)}
                AND Time < {Sanitize(_timeRange.Item2)}
                ORDER BY Time;
            ";
        }

        protected abstract IMarketData MakeDataFromResults(IDataRecord record);
    }

    internal abstract class InsertCommand<TMarketData> : DbCommand<bool>
    {
        protected string _symbol;
        IEnumerable<TMarketData> _dataCollection;
        protected string _marketDataName;
        internal int NbInserted = 0;

        public InsertCommand(string symbol, IEnumerable<TMarketData> dataCollection, SqliteConnection connection) : base(connection)
        {
            _symbol = symbol;
            _dataCollection = dataCollection;
            _marketDataName = typeof(TMarketData).Name;
        }

        public override bool Execute()
        {
            using var transaction = _connection.BeginTransaction();

            SqliteCommand insertCmd = _connection.CreateCommand();
            Insert(insertCmd, "Stock", "Symbol", _symbol);

            int nbInserted = InsertMarketData(insertCmd, _dataCollection);

            transaction.Commit();
            NbInserted = nbInserted;

            return true;
        }

        protected virtual int InsertMarketData(SqliteCommand insertCmd, IEnumerable<TMarketData> dataCollection)
        {
            int nbInserted = 0;
            foreach (TMarketData data in _dataCollection)
            {
                nbInserted += InsertMarketData(insertCmd, data);
            }

            return nbInserted;
        }

        protected virtual int Insert(SqliteCommand command, string tableName, string column, object value)
        {
            command.CommandText =
            $@"
                INSERT OR IGNORE INTO {tableName} ({column})
                VALUES ({Sanitize(value)})
            ";

            return command.ExecuteNonQuery();
        }

        protected virtual int Insert(SqliteCommand command, string tableName, IEnumerable<string> columns, IEnumerable<object> values)
        {
            command.CommandText =
            $@"
                INSERT OR IGNORE INTO {tableName} ({string.Join(',', columns)})
                VALUES ({string.Join(',', values.Select(Sanitize))})
            ";

            return command.ExecuteNonQuery();
        }

        protected abstract int InsertMarketData(SqliteCommand command, TMarketData data);
    }
}
