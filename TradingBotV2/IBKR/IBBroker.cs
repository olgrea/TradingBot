using System.Diagnostics;
using TradingBotV2.Broker;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.Orders;

namespace TradingBotV2.IBKR
{
    internal class IBBroker : IBroker
    {
        IBClient _client;
        int _port;
        int _clientId;
        int _reqId = 0;
        int _nextValidOrderId = 0;

        public IBBroker(int clientId = 1337)
        {
            _port = GetPort();
            _clientId = clientId;
            _client = new IBClient();
        }

        public IMarketDataProvider MarketDataProvider => throw new NotImplementedException();

        public IOrderManager OrderManager => throw new NotImplementedException();

        void Init(int clientId)
        {

        }

        int GetPort()
        {
            var ibGatewayProc = Process.GetProcessesByName("ibgateway").FirstOrDefault();
            if (ibGatewayProc != null)
                return IBClient.DefaultIBGatewayPort;

            var twsProc = Process.GetProcessesByName("tws").FirstOrDefault();
            if (twsProc != null)
                return IBClient.DefaultTWSPort;

            throw new ArgumentException("Neither TWS Workstation or IB Gateway is running.");
        }

        public async Task<string> ConnectAsync()
        {
            return await ConnectAsync(TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 5000), CancellationToken.None);
        }

        async Task<string> ConnectAsync(TimeSpan timeout, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<string>();
            token.Register(() => tcs.TrySetCanceled());

            var nextValidId = new Action<int>(id =>
            {
                //_logger.Trace($"ConnectAsync : next valid id {id}");
                _nextValidOrderId = id;
            });

            var managedAccounts = new Action<IEnumerable<string>>(accList =>
            {
                if (accList.Count() > 1)
                    tcs.SetException(new NotSupportedException("Only single account structures are supported."));

                //_logger.Trace($"ConnectAsync : managedAccounts {accList} - set result");
                tcs.TrySetResult(accList.First());
            });

            var error = new Action<ErrorMessage>(msg => tcs.TrySetException(msg));

            _client.Responses.NextValidId += nextValidId;
            _client.Responses.ManagedAccounts += managedAccounts;
            _client.Responses.Error += error;

            try
            {
                _ = Task.Delay(timeout, token).ContinueWith(t => tcs.TrySetException(new TimeoutException($"{nameof(ConnectAsync)}")));
                _client.Connect(IBClient.DefaultIP, _port, _clientId);
                await tcs.Task;

            }
            finally
            {
                _client.Responses.NextValidId -= nextValidId;
                _client.Responses.ManagedAccounts -= managedAccounts;
                _client.Responses.Error -= error;
            }

            return tcs.Task.Result;
        }

        public async Task DisconnectAsync()
        {
            var tcs = new TaskCompletionSource();
            //_logger.Debug($"Disconnecting from TWS");

            var disconnect = new Action(() => tcs.TrySetResult());
            var error = new Action<ErrorMessage>(msg => tcs.TrySetException(msg));

            _client.Responses.ConnectionClosed += disconnect;
            _client.Responses.Error += error;
            try
            {
                _client.Disconnect();
                await tcs.Task;
            }
            finally
            {
                _client.Responses.ConnectionClosed -= disconnect;
                _client.Responses.Error -= error;
            }
        }

        public Task<Account> GetAccountAsync(string accountCode)
        {
            throw new NotImplementedException();
        }
    }
}
