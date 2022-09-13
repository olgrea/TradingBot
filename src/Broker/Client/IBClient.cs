using System.Collections.Generic;
using System.Threading.Tasks;
using IBApi;
using TradingBot.Utils;

namespace TradingBot.Broker.Client
{
    internal class IBClient : IClient
    {
        static HashSet<int> _clientIds = new HashSet<int>();

        const int DefaultPort = 7496;
        const string DefaultIP = "127.0.0.1";
        int _clientId = 1337;

        int _nextValidOrderId = -1;
        int _reqId = 0;

        EClientSocket _clientSocket;
        EReaderSignal _signal;
        EReader _reader;
        TWSCallbacks _IBCallbacks;

        ILogger _logger;
        Task _processMsgTask;

        public IBClient(int clientId, ILogger logger)
        {
            _clientId = clientId;
            _logger = logger;
        }
    }
}
