using Microsoft.Data.Sqlite;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.DataStorage.Sqlite.DbCommands;

namespace TradingBotV2.DataStorage.Sqlite.DbCommandFactories
{
    public abstract class DbCommandFactory<TMarketData> where TMarketData : IMarketData, new()
    {
        protected SqliteConnection _connection;

        protected DbCommandFactory(string dbPath)
        {
            if (!File.Exists(dbPath))
            {
                throw new FileNotFoundException(dbPath);
            }

            _connection = new SqliteConnection("Data Source=" + dbPath);
            _connection.Open();
        }

        ~DbCommandFactory()
        {
            _connection?.Close();
            _connection?.Dispose();
        }

        public abstract DbCommand<bool> CreateExistsCommand(string symbol, DateTime date);
        public abstract DbCommand<bool> CreateExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange);
        public abstract DbCommand<IEnumerable<TMarketData>> CreateSelectCommand(string symbol, DateTime date);
        public abstract DbCommand<IEnumerable<TMarketData>> CreateSelectCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange);
        public abstract DbCommand<bool> CreateInsertCommand(string symbol, IEnumerable<TMarketData> dataCollection);
    }
}
