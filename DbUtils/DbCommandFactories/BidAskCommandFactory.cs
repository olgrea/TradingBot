using System;
using System.Collections.Generic;
using DbUtils.DbCommands;
using Microsoft.Data.Sqlite;
using TradingBot.Broker.MarketData;

namespace DbUtils.DbCommandFactories
{
    public class BidAskCommandFactory : DbCommandFactory<BidAsk>
    {
        public BidAskCommandFactory(SqliteConnection connection) : base(connection)
        {
        }

        protected override DbCommand<bool> CreateExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new BidAskExistsCommand(symbol, date, timeRange, _connection);
        }

        protected override DbCommand<IEnumerable<BidAsk>> CreateSelectCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new SelectBidAsksCommand(symbol, date, timeRange, _connection);
        }

        protected override DbCommand<bool> CreateInsertCommand(string symbol, IEnumerable<BidAsk> dataCollection)
        {
            return new InsertBidAsksCommand(symbol, dataCollection, _connection);
        }
    }
}
