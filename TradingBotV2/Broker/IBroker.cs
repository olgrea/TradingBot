using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.MarketData.Providers;
using TradingBotV2.Broker.Orders;

namespace TradingBotV2.Broker
{
    public interface IBroker
    {
        public event Action<AccountValue>? AccountValueUpdated;
        public event Action<Position>? PositionUpdated;
        public event Action<PnL>? PnLUpdated;
        public event Action<Exception>? ErrorOccured;

        public ILiveDataProvider LiveDataProvider { get; }
        public IHistoricalDataProvider HistoricalDataProvider { get; }
        public IOrderManager OrderManager { get; }

        public Task<string> ConnectAsync();
        public Task DisconnectAsync();
        public Task<Account> GetAccountAsync();
        public void RequestAccountUpdates();
        public void CancelAccountUpdates();

        public Task<DateTime> GetServerTimeAsync();
    }
}
