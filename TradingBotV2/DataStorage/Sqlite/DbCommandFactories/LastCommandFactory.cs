using TradingBotV2.Broker.MarketData;
using TradingBotV2.DataStorage.Sqlite.DbCommands;

namespace TradingBotV2.DataStorage.Sqlite.DbCommandFactories
{
    public class LastCommandFactory : DbCommandFactory
    {
        public LastCommandFactory(string dbPath) : base(dbPath)
        {
        }

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateTime date)
        {
            return new LastExistsCommand(symbol, date, MarketDataUtils.MarketDayTimeRange, _connection);
        }

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new LastExistsCommand(symbol, date, timeRange, _connection);
        }

        public override DbCommand<IEnumerable<IMarketData>> CreateSelectCommand(string symbol, DateTime date)
        {
            return new SelectLastsCommand(symbol, date, MarketDataUtils.MarketDayTimeRange, _connection);
        }

        public override DbCommand<IEnumerable<IMarketData>> CreateSelectCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            return new SelectLastsCommand(symbol, date, timeRange, _connection);
        }

        public override DbCommand<bool> CreateInsertCommand(string symbol, IEnumerable<IMarketData> dataCollection)
        {
            return new InsertLastsCommand(symbol, dataCollection.Cast<Last>(), _connection);
        }
    }
}
