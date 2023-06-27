using Broker.DataStorage.Sqlite.DbCommands;
using Broker.MarketData;
using Broker.Utils;

namespace Broker.DataStorage.Sqlite.DbCommandFactories
{
    public class BarCommandFactory : DbCommandFactory
    {
        BarLength _barLength;

        public BarCommandFactory(string dbPath) : base(dbPath)
        {
            _barLength = BarLength._1Sec;
        }

        public override DbCommand<bool> CreateExistsCommand(string symbol, DateRange dateRange)
        {
            return new BarExistsCommand(symbol, dateRange, _barLength, _connection);
        }

        public override DbCommand<IEnumerable<IMarketData>> CreateSelectCommand(string symbol, DateRange dateRange)
        {
            return new SelectBarsCommand(symbol, dateRange, _barLength, _connection);
        }

        public override DbCommand<int> CreateInsertCommand(string symbol, DateRange dateRange, IEnumerable<IMarketData> dataCollection)
        {
            return new InsertBarsCommand(symbol, dateRange, dataCollection.Cast<Bar>(), _connection);
        }
    }
}
