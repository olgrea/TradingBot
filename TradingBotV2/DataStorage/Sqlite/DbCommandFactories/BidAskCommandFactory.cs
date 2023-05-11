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

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateTime date)
        {
            return new BidAskExistsCommand(symbol, date, MarketDataUtils.MarketDayTimeRange, _connection);
        }

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new BidAskExistsCommand(symbol, date, timeRange, _connection);
        }

        public override DbCommand<IEnumerable<IMarketData>> CreateSelectCommand(string symbol, DateTime date)
        {
            return new SelectBidAsksCommand(symbol, date, MarketDataUtils.MarketDayTimeRange, _connection);
        }

        public override DbCommand<IEnumerable<IMarketData>> CreateSelectCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new SelectBidAsksCommand(symbol, date, timeRange, _connection);
        }

        public override DbCommand<bool> CreateInsertCommand(string symbol, TimeRange timerange, IEnumerable<IMarketData> dataCollection)
        {
            return new InsertBidAsksCommand(symbol, timerange, dataCollection.Cast<BidAsk>(), _connection);
        }
    }
}
