using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.DataStorage.Sqlite.DbCommands;

namespace TradingBotV2.DataStorage.Sqlite.DbCommandFactories
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
            return CreateExistsCommand(symbol, date, MarketDataUtils.MarketDayTimeRange);
        }

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new BarExistsCommand(symbol, date, timeRange, _barLength, _connection);
        }

        public override DbCommand<IEnumerable<Bar>> CreateSelectCommand(string symbol, DateTime date)
        {
            return new SelectBarsCommand(symbol, date, MarketDataUtils.MarketDayTimeRange, _barLength, _connection);
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
