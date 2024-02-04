using System.Runtime.CompilerServices;
using Broker.Accounts;
using Broker.IBKR.Accounts;
using Broker.IBKR.Client;
using Broker.IBKR.Orders;
using Broker.IBKR.Providers;
using Broker.Orders;

[assembly: InternalsVisibleTo("Broker.Tests")]
[assembly: InternalsVisibleTo("Trader.Tests")]
namespace Broker.IBKR
{
    public interface IIBBroker : IBroker
    {
        public event Action<AccountValue>? AccountValueUpdated;
        public event Action<Position>? PositionUpdated;
        public event Action<PnL>? PnLUpdated;
        public event Action<Exception>? ErrorOccured;
        public event Action<Message>? MessageReceived;

        public ILiveDataProvider LiveDataProvider { get; }
        public IIBHistoricalDataProvider HistoricalDataProvider { get; }
        public IOrderManager<IBOrder> OrderManager { get; }

        public void RequestAccountUpdates();
        public void CancelAccountUpdates();

        public Task<DateTime> GetServerTimeAsync();
        public Task<DateTime> GetServerTimeAsync(CancellationToken token);
    }
}
