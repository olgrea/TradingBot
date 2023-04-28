using IBApi;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.MarketData.Providers;
using TradingBotV2.Broker.Orders;

namespace TradingBotV2.Broker
{
    public interface IBroker
    {
        public event Action<Account>? AccountUpdated;

        public ILiveDataProvider LiveDataProvider { get; }
        public IHistoricalDataProvider HistoricalDataProvider { get; }
        public IOrderManager OrderManager { get; }

        public Task<string> ConnectAsync();
        public Task DisconnectAsync();
        public Task<Account> GetAccountAsync(string accountCode);
        public void RequestAccountUpdates(string account);
        public void CancelAccountUpdates(string account);
    }
}
