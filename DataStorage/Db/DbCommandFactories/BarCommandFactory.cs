using System;
using System.Collections.Generic;
using DataStorage.Db.DbCommands;
using InteractiveBrokers.MarketData;
using Microsoft.Data.Sqlite;

namespace DataStorage.Db.DbCommandFactories
{
    public class BarCommandFactory : DbCommandFactory<Bar>
    {
        BarLength _barLength;

        public BarCommandFactory(BarLength barLength, SqliteConnection connection = null) : base(connection)
        {
            _barLength = barLength;
        }

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateTime date)
        {
            return CreateExistsCommand(symbol, date, Utils.MarketDayTimeRange);
        }

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new BarExistsCommand(symbol, date, timeRange, _barLength, _connection);
        }

        public override DbCommand<IEnumerable<Bar>> CreateSelectCommand(string symbol, DateTime date)
        {
            return new SelectBarsCommand(symbol, date, Utils.MarketDayTimeRange, _barLength, _connection);
        }

        public override DbCommand<IEnumerable<Bar>> CreateSelectCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new SelectBarsCommand(symbol, date, timeRange, _barLength, _connection);
        }

        public override DbCommand<bool> CreateInsertCommand(string symbol, IEnumerable<Bar> dataCollection)
        {
            return new InsertBarsCommand(symbol, dataCollection, _connection);
        }
    }
}
