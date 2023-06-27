using Broker.DataStorage.Sqlite.DbCommands;
using Broker.MarketData;
using Broker.Utils;
using Microsoft.Data.Sqlite;

namespace Broker.DataStorage.Sqlite.DbCommandFactories
{
    public abstract class DbCommandFactory
    {
        protected SqliteConnection _connection;
        string _dbPath;

        protected DbCommandFactory(string dbPath)
        {
            if (!File.Exists(dbPath))
            {
                throw new FileNotFoundException(dbPath);
            }

            _dbPath = dbPath;
            _connection = new SqliteConnection("Data Source=" + dbPath);
            OpenConnection();
        }

        ~DbCommandFactory()
        {
            CloseConnection();
        }

        internal string DbPath => _dbPath;
        internal SqliteConnection Connection => _connection;
        internal void OpenConnection() => _connection.Open();
        internal void CloseConnection()
        {
            _connection.Close();
            _connection.Dispose();
        }

        public static DbCommandFactory Create<TData>(string dbPath) where TData : IMarketData, new()
        {
            if (typeof(TData) == typeof(Bar))
                return new BarCommandFactory(dbPath);
            else if (typeof(TData) == typeof(BidAsk))
                return new BidAskCommandFactory(dbPath);
            else if (typeof(TData) == typeof(Last))
                return new LastCommandFactory(dbPath);
            else
                throw new NotImplementedException($"factory for market data type {typeof(TData).Name} is not implemented.");
        }

        public abstract DbCommand<bool> CreateExistsCommand(string symbol, DateRange dateRange);
        public abstract DbCommand<IEnumerable<IMarketData>> CreateSelectCommand(string symbol, DateRange dateRange);
        public abstract DbCommand<int> CreateInsertCommand(string symbol, DateRange dateRange, IEnumerable<IMarketData> dataCollection);
    }
}
