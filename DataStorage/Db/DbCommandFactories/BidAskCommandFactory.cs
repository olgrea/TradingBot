using System;
using System.Collections.Generic;
using DataStorage.Db.DbCommands;
using InteractiveBrokers.MarketData;
using Microsoft.Data.Sqlite;

namespace DataStorage.Db.DbCommandFactories
{
    public class BidAskCommandFactory : DbCommandFactory<BidAsk>
    {
        public BidAskCommandFactory(SqliteConnection connection = null) : base(connection)
        {
        }

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateTime date)
        {
            return new BidAskExistsCommand(symbol, date, Utils.MarketDayTimeRange, _connection);
        }

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new BidAskExistsCommand(symbol, date, timeRange, _connection);
        }

        public override DbCommand<IEnumerable<BidAsk>> CreateSelectCommand(string symbol, DateTime date)
        {
            return new SelectBidAsksCommand(symbol, date, Utils.MarketDayTimeRange, _connection);
        }

        public override DbCommand<IEnumerable<BidAsk>> CreateSelectCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new SelectBidAsksCommand(symbol, date, timeRange, _connection);
        }

        public override DbCommand<bool> CreateInsertCommand(string symbol, IEnumerable<BidAsk> dataCollection)
        {
            return new InsertBidAsksCommand(symbol, dataCollection, _connection);
        }
    }
}
