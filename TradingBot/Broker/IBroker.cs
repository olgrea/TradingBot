using TradingBot.Broker.Accounts;
using TradingBot.Broker.MarketData.Providers;
using TradingBot.Broker.Orders;
using TradingBot.IBKR.Client;

namespace TradingBot.Broker
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
        public void RequestAccountUpdates();
        public void CancelAccountUpdates();

        public Task<DateTime> GetServerTimeAsync();
    }
}
