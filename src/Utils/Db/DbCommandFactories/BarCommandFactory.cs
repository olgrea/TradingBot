using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using TradingBot.Broker.MarketData;
using TradingBot.Utils.Db.DbCommands;

namespace TradingBot.Utils.Db.DbCommandFactories
{
    public class BarCommandFactory : DbCommandFactory<Bar>
    {
        BarLength _barLength;

        public BarCommandFactory(BarLength barLength, SqliteConnection connection = null) : base(connection)
        {
            _barLength = barLength;
        }

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new BarExistsCommand(symbol, date, timeRange, _barLength, _connection);
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
