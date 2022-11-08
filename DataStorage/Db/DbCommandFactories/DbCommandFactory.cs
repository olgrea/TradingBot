using System;
using System.Collections.Generic;
using DataStorage.Db.DbCommands;
using InteractiveBrokers.MarketData;
using Microsoft.Data.Sqlite;

namespace DataStorage.Db.DbCommandFactories
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
