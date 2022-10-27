using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using TradingBot.Broker.MarketData;
using TradingBot.Utils.Db.DbCommands;

namespace TradingBot.Utils.Db.DbCommandFactories
{
    public abstract class DbCommandFactory<TMarketData>
    {
        protected SqliteConnection _connection;

        protected DbCommandFactory(SqliteConnection connection)
        {
            _connection = connection;
        }

        protected abstract DbCommand<bool> CreateExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange);
        protected abstract DbCommand<IEnumerable<TMarketData>> CreateSelectCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange);
        protected abstract DbCommand<bool> CreateInsertCommand(string symbol, IEnumerable<TMarketData> dataCollection);
    }
}
