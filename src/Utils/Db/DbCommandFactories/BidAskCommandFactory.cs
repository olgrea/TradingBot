using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using TradingBot.Broker.MarketData;
using TradingBot.Utils.Db.DbCommands;

namespace TradingBot.Utils.Db.DbCommandFactories
{
    public class BidAskCommandFactory : DbCommandFactory<BidAsk>
    {
        public BidAskCommandFactory(SqliteConnection connection = null) : base(connection)
        {
        }

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new BidAskExistsCommand(symbol, date, timeRange, _connection);
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
