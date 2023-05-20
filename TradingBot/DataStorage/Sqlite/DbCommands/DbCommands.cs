using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using TradingBot.Broker.MarketData;
using TradingBot.Utils;

namespace TradingBot.DataStorage.Sqlite.DbCommands
{
    // *** OF NOTE : 
    // A market data row with NULL values (except for Ticker and DateTime. These should never be NULL) indicates that the data for 
    // a specific timestamp was attempted to be retrieved from the server but doesn't exist. 
    // Examples :
    // - when a stock has been halted, no bars will exists during the halt
    // - for last traded prices, it's possible that no trade occured at a specific time
    // - The Bid/Ask may not have changed for a specific time
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
            else if (value is decimal dec)
            {
                return dec.ToString(CultureInfo.InvariantCulture);
            }
            else
                return value?.ToString() ?? string.Empty;
        }
    }

    internal abstract class ExistsCommand<TMarketData> : DbCommand<bool>
    {
        protected string _symbol;
        protected DateRange _dateRange;
        protected string _marketDataName;

        public ExistsCommand(string symbol, DateRange dateRange, SqliteConnection connection) : base(connection)
        {
            _symbol = symbol;
            _dateRange = dateRange;
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
            var nbTimestamps = (_dateRange.To - _dateRange.From).TotalSeconds;
            return
            $@"
                SELECT EXISTS (
                    SELECT 1 WHERE 
                        (SELECT COUNT(DISTINCT DateTime) FROM {_marketDataName}s
                            LEFT JOIN Tickers ON Tickers.Id = {_marketDataName}s.Ticker
                            WHERE Symbol = {Sanitize(_symbol)}
                            AND DateTime >= {Sanitize(_dateRange.From.ToUnixTimeSeconds())} 
                            AND DateTime < {Sanitize(_dateRange.To.ToUnixTimeSeconds())} 
                        ) = {Sanitize(nbTimestamps)}
                );
            ";
        }
    }

    internal abstract class SelectCommand<TMarketData> : DbCommand<IEnumerable<IMarketData>> where TMarketData : IMarketData, new()
    {
        protected string _symbol;
        protected DateRange _dateRange;
        protected string _marketDataName;

        public SelectCommand(string symbol, DateRange dateRange, SqliteConnection connection) : base(connection)
        {
            _symbol = symbol;
            _dateRange = dateRange;
            _marketDataName = typeof(TMarketData).Name;
        }

        public override IEnumerable<IMarketData> Execute()
        {
            SqliteCommand command = _connection.CreateCommand();
            command.CommandText = MakeSelectCommandText();
            using SqliteDataReader reader = command.ExecuteReader();
            return new LinkedList<IMarketData>(reader.Cast<IDataRecord>().Select(MakeDataFromResults));
        }

        protected abstract string MakeSelectCommandText();

        protected abstract IMarketData MakeDataFromResults(IDataRecord record);
    }

    internal abstract class InsertCommand<TMarketData> : DbCommand<int> where TMarketData : IMarketData, new()
    {
        protected string _symbol;
        protected DateRange _dateRange;
        protected IDictionary<DateTime, List<TMarketData>> _dataDict;
        protected string _marketDataName;
        internal int NbInserted = 0;

        public InsertCommand(string symbol, DateRange dateRange, IEnumerable<TMarketData> dataCollection, SqliteConnection connection) : base(connection)
        {
            _symbol = symbol;
            _dateRange = dateRange;

            _dataDict = new Dictionary<DateTime, List<TMarketData>>();
            foreach (var data in dataCollection)
            {
                if(!_dataDict.ContainsKey(data.Time))
                    _dataDict[data.Time] = new List<TMarketData>();
                _dataDict[data.Time].Add(data);
            }

            _marketDataName = typeof(TMarketData).Name;
        }

        public override int Execute()
        {
            using var transaction = _connection.BeginTransaction();

            SqliteCommand insertCmd = _connection.CreateCommand();
            Insert(insertCmd, "Tickers", "Symbol", _symbol);

            int nbInserted = InsertMarketData(insertCmd);

            transaction.Commit();
            NbInserted = nbInserted;

            return NbInserted;
        }

        protected virtual int InsertMarketData(SqliteCommand insertCmd)
        {
            int nbInserted = 0;

            for (DateTime i = _dateRange.From; i < _dateRange.To; i = i.AddSeconds(1))
            {
                if(_dataDict.TryGetValue(i, out List<TMarketData>? data))
                {
                    foreach (var d in data)
                        nbInserted += InsertMarketData(insertCmd, d);
                }
                else
                {
                    InsertNullMarketData(insertCmd, i);
                }
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
        protected abstract int InsertNullMarketData(SqliteCommand command, DateTime dateTime);
    }
}
