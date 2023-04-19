using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.DataStorage.Sqlite.DbCommands;

namespace TradingBotV2.DataStorage.Sqlite.DbCommandFactories
{
    public abstract class DbCommandFactory
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

        public static DbCommandFactory Create<TData>(string dbPath) where TData : IMarketData, new()
        {
            if (typeof(TData) == typeof(Bar))
                return new BarCommandFactory(BarLength._1Sec, dbPath);
            else if (typeof(TData) == typeof(BidAsk))
                return new BidAskCommandFactory(dbPath);
            else if (typeof(TData) == typeof(Last))
                return new LastCommandFactory(dbPath);
            else
                throw new NotImplementedException($"factory for market data type {typeof(TData).Name} is not implemented.");
        }

        public abstract DbCommand<bool> CreateExistsCommand(string symbol, DateTime date);
        public abstract DbCommand<bool> CreateExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange);
        public abstract DbCommand<IEnumerable<IMarketData>> CreateSelectCommand(string symbol, DateTime date);
        public abstract DbCommand<IEnumerable<IMarketData>> CreateSelectCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange);
        public abstract DbCommand<bool> CreateInsertCommand(string symbol, IEnumerable<IMarketData> dataCollection);
    }
}
