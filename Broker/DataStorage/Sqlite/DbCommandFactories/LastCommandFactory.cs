using Broker.DataStorage.Sqlite.DbCommands;
using Broker.MarketData;
using Broker.Utils;

namespace Broker.DataStorage.Sqlite.DbCommandFactories
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

        public override DbCommand<int> CreateInsertCommand(string symbol, DateRange dateRange, IEnumerable<IMarketData> dataCollection)
        {
            return new InsertLastsCommand(symbol, dateRange, dataCollection.Cast<Last>(), _connection);
        }
    }
}
