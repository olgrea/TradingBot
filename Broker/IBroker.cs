using System.Runtime.CompilerServices;
using Broker.Accounts;

[assembly: InternalsVisibleTo("Broker.Tests")]
namespace Broker
{
    public interface IBroker
    {
        public Task<string> ConnectAsync();
        public Task<string> ConnectAsync(CancellationToken token);
        public Task DisconnectAsync();
        public Task<Account> GetAccountAsync();
        public Task<Account> GetAccountAsync(CancellationToken token);
    }
}
