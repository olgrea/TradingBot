using TradingBotV2.Broker.MarketData;
using TradingBotV2.DataStorage.Sqlite.DbCommands;
using TradingBotV2.Utils;

namespace TradingBotV2.DataStorage.Sqlite.DbCommandFactories
{
    public class BidAskCommandFactory : DbCommandFactory
    {
        public BidAskCommandFactory(string dbPath) : base(dbPath)
        {
        }

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateRange dateRange)
        {
            return new BidAskExistsCommand(symbol, dateRange, _connection);
        }

        public override DbCommand<IEnumerable<IMarketData>> CreateSelectCommand(string symbol, DateRange dateRange)
        {
            return new SelectBidAsksCommand(symbol, dateRange, _connection);
        }

        public override DbCommand<bool> CreateInsertCommand(string symbol, DateRange dateRange, IEnumerable<IMarketData> dataCollection)
        {
            return new InsertBidAsksCommand(symbol, dateRange, dataCollection.Cast<BidAsk>(), _connection);
        }
    }
}
