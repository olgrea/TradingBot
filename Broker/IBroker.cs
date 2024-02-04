using System.Runtime.CompilerServices;
using Broker.Accounts;
using Broker.IBKR.Client;

[assembly: InternalsVisibleTo("Broker.Tests")]
namespace Broker
{
    public interface IBroker
    {
        public Task<string> ConnectAsync();
        public Task<string> ConnectAsync(CancellationToken token);
        public Task DisconnectAsync();
        public Task<IAccount> GetAccountAsync();
        public Task<IAccount> GetAccountAsync(CancellationToken token);
    }
}
