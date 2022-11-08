using InteractiveBrokers;
using InteractiveBrokers.Backend;

namespace Backtester
{
    internal class BacktesterClient : IBClient
    {
        public BacktesterClient(int clientId, IIBClientSocket socket) 
            : base(clientId, socket)
        {

        }
    }
}
