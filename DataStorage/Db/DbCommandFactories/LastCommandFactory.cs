using System;
using System.Collections.Generic;
using DataStorage.Db.DbCommands;
using InteractiveBrokers.MarketData;
using Microsoft.Data.Sqlite;

namespace DataStorage.Db.DbCommandFactories
{
    public class LastCommandFactory : DbCommandFactory<Last>
    {
        public LastCommandFactory(SqliteConnection connection = null) : base(connection)
        {
        }

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateTime date)
        {
            return new LastExistsCommand(symbol, date, MarketDataUtils.MarketDayTimeRange, _connection);
        }

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new LastExistsCommand(symbol, date, timeRange, _connection);
        }

        public override DbCommand<IEnumerable<Last>> CreateSelectCommand(string symbol, DateTime date)
        {
            return new SelectLastsCommand(symbol, date, MarketDataUtils.MarketDayTimeRange, _connection);
        }

        public override DbCommand<IEnumerable<Last>> CreateSelectCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new SelectLastsCommand(symbol, date, timeRange, _connection);
        }

        public override DbCommand<bool> CreateInsertCommand(string symbol, IEnumerable<Last> dataCollection)
        {
            return new InsertLastsCommand(symbol, dataCollection, _connection);
        }
    }
}
