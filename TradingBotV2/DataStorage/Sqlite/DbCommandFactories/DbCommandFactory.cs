using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.DataStorage.Sqlite;
using TradingBotV2.DataStorage.Sqlite.DbCommands;

namespace TradingBotV2.DataStorage.Sqlite.DbCommandFactories
{
    public abstract class DbCommandFactory<TMarketData> where TMarketData : IMarketData, new()
    {
        protected SqliteConnection _connection;

        protected DbCommandFactory(SqliteConnection connection = null)
        {
            if (connection == null)
            {
                connection = new SqliteConnection(Constants.DbConnectionString);
                connection.Open();
            }

            _connection = connection;
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
