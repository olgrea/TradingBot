using System;
using System.Collections.Generic;
using DbUtils.DbCommands;
using Microsoft.Data.Sqlite;
using TradingBot.Broker.MarketData;

namespace DbUtils.DbCommandFactories
{
    public class BarCommandFactory : DbCommandFactory<Bar>
    {
        BarLength _barLength;

        public BarCommandFactory(BarLength barLength, SqliteConnection connection) : base(connection)
        {
            _barLength = barLength;
        }

        protected override DbCommand<bool> CreateExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new BarExistsCommand(symbol, date, timeRange, _barLength, _connection);
        }

        protected override DbCommand<IEnumerable<Bar>> CreateSelectCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new SelectBarsCommand(symbol, date, timeRange, _barLength, _connection);
        }

        protected override DbCommand<bool> CreateInsertCommand(string symbol, IEnumerable<Bar> dataCollection)
        {
            return new InsertBarsCommand(symbol, dataCollection, _connection);
        }
    }
}
