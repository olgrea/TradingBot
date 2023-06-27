using Broker.Accounts;
using Broker.IBKR.Client;
using Broker.MarketData.Providers;
using Broker.Orders;

namespace Broker
{
    public interface IBroker
    {
        public event Action<AccountValue>? AccountValueUpdated;
        public event Action<Position>? PositionUpdated;
        public event Action<PnL>? PnLUpdated;
        public event Action<Exception>? ErrorOccured;
        public event Action<Message>? MessageReceived;

        public ILiveDataProvider LiveDataProvider { get; }
        public IHistoricalDataProvider HistoricalDataProvider { get; }
        public IOrderManager OrderManager { get; }

        public Task<string> ConnectAsync();
        public Task<string> ConnectAsync(CancellationToken token);
        public Task DisconnectAsync();
        public Task<Account> GetAccountAsync();
        public Task<Account> GetAccountAsync(CancellationToken token);
        public void RequestAccountUpdates();
        public void CancelAccountUpdates();

        public Task<DateTime> GetServerTimeAsync();
        public Task<DateTime> GetServerTimeAsync(CancellationToken token);
    }
}
