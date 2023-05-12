using TradingBotV2.Broker.MarketData;
using TradingBotV2.DataStorage.Sqlite.DbCommands;
using TradingBotV2.Utils;

namespace TradingBotV2.DataStorage.Sqlite.DbCommandFactories
{
    public class LastCommandFactory : DbCommandFactory
    {
        public LastCommandFactory(string dbPath) : base(dbPath)
        {
        }

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateRange dateRange)
        {
            return new LastExistsCommand(symbol, dateRange, _connection);
        }

        public override DbCommand<IEnumerable<IMarketData>> CreateSelectCommand(string symbol, DateRange dateRange)
        {
            return new SelectLastsCommand(symbol, dateRange, _connection);
        }

        public override DbCommand<bool> CreateInsertCommand(string symbol, DateRange dateRange, IEnumerable<IMarketData> dataCollection)
        {
            return new InsertLastsCommand(symbol, dateRange, dataCollection.Cast<Last>(), _connection);
        }
    }
}
