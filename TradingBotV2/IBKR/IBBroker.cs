using System.Diagnostics;
using System.Globalization;
using NLog;
using TradingBotV2.Broker;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.Contracts;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.MarketData.Providers;
using TradingBotV2.Broker.Orders;

namespace TradingBotV2.IBKR
{
    public class IBBroker : IBroker
    {        
        IBClient _client;
        int _port;
        int _clientId;

        public IBBroker(int clientId) : this(clientId, null) {}

        public IBBroker(int clientId, ILogger logger)
        {
            _port = GetPort();
            _clientId = clientId;
            _client = new IBClient();

            LiveDataProvider = new IBLiveDataProvider(_client);
            HistoricalDataProvider = new IBHistoricalDataProvider(_client, logger);
        }

        internal IBClient Client => _client;
        public ILiveDataProvider LiveDataProvider { get; init; }
        public IHistoricalDataProvider HistoricalDataProvider { get; init; }

        public IOrderManager OrderManager => throw new NotImplementedException();

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
            // awaiting a TaskCompletionSource's task doesn't return on the main thread without this flag.

            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            int nextId = -1;
            string account = string.Empty;

            var nextValidId = new Action<int>(id =>
            {
                //_logger.Trace($"ConnectAsync : next valid id {id}");
                Debug.Assert(id > 0);
                nextId = id;
                if(nextId > 0 && !string.IsNullOrEmpty(account))
                    tcs.TrySetResult(account);
            });

            var managedAccounts = new Action<IEnumerable<string>>(accList =>
            {
                if (accList.Count() > 1)
                    tcs.SetException(new NotSupportedException("Only single account structures are supported."));

                //_logger.Trace($"ConnectAsync : managedAccounts {accList} - set result");
                account = accList.First();
                if (nextId > 0 && !string.IsNullOrEmpty(account))
                    tcs.TrySetResult(account);
            });

            var error = new Action<ErrorMessage>(msg => tcs.TrySetException(msg));

            _client.Responses.NextValidId += nextValidId;
            _client.Responses.ManagedAccounts += managedAccounts;
            _client.Responses.Error += error;

            try
            {
                if(timeout.Milliseconds > 0)
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
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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

        async Task<IEnumerable<string>> GetManagedAccountsList()
        {
            var tcs = new TaskCompletionSource<IEnumerable<string>>(TaskCreationOptions.RunContinuationsAsynchronously);

            var managedAccount = new Action<IEnumerable<string>>(list => tcs.SetResult(list));
            var error = new Action<ErrorMessage>(msg => tcs.TrySetException(msg));

            _client.Responses.ManagedAccounts += managedAccount;
            try
            {
                _client.RequestManagedAccounts();
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.ManagedAccounts -= managedAccount;
                _client.Responses.Error -= error;
            }
        }

        // In a single account structure, the account number is ignored.
        public async Task<Account> GetAccountAsync(string accountCode = null)
        {
            var accList = await GetManagedAccountsList();
            if(string.IsNullOrEmpty(accountCode))
            {
                if (accList.Count() > 1)
                {
                    throw new ArgumentException("An account code must be specified for multiple-account structures.");
                }
                accountCode = accList.First();
            }
            else if (!accList.Contains(accountCode))
            {
                throw new ArgumentException($"The account code \"{accountCode}\" doesn't exists.");
            }

            var account = new Account();
            var tcs = new TaskCompletionSource<Account>(TaskCreationOptions.RunContinuationsAsynchronously);

            var updateAccountTime = new Action<TimeSpan>(time =>
            {
                //_logger.Trace($"GetAccountAsync updateAccountTime : {time}");
                account.Time = time;
            });
            var updateAccountValue = new Action<IBApi.AccountValue>(accValue =>
            {
                //_logger.Trace($"GetAccountAsync updateAccountValue : key={accValue.Key}, value={accValue.Value}");
                switch (accValue.Key)
                {
                    case "AccountReady":
                        if (!bool.Parse(accValue.Value))
                        {
                            string msg = "Account not available at the moment. The IB server is in the process of resetting. Values returned may not be accurate.";
                            //_logger.Warn(msg);
                        }
                        break;

                    case "CashBalance":
                        account.CashBalances[accValue.Currency] = double.Parse(accValue.Value, CultureInfo.InvariantCulture);
                        break;

                    case "RealizedPnL":
                        account.RealizedPnL[accValue.Currency] = double.Parse(accValue.Value, CultureInfo.InvariantCulture);
                        break;

                    case "UnrealizedPnL":
                        account.UnrealizedPnL[accValue.Currency] = double.Parse(accValue.Value, CultureInfo.InvariantCulture);
                        break;
                }
            });
            var updatePortfolio = new Action<IBApi.Position>(pos =>
            {
                //_logger.Trace($"GetAccountAsync updatePortfolio : {pos}");
                account.Positions.Add((Position)pos);
            });
            var accountDownloadEnd = new Action<string>(accountCode =>
            {
                //_logger.Trace($"GetAccountAsync accountDownloadEnd : {accountCode} - set result");
                account.Code = accountCode;
                tcs.SetResult(account);
            });

            _client.Responses.UpdateAccountTime += updateAccountTime;
            _client.Responses.UpdateAccountValue += updateAccountValue;
            _client.Responses.UpdatePortfolio += updatePortfolio;
            _client.Responses.AccountDownloadEnd += accountDownloadEnd;

            try
            {
                _client.RequestAccountUpdates(accountCode);
                return await tcs.Task;
            }
            finally
            {
                _client.CancelAccountUpdates(accountCode);

                _client.Responses.UpdateAccountTime -= updateAccountTime;
                _client.Responses.UpdateAccountValue -= updateAccountValue;
                _client.Responses.UpdatePortfolio -= updatePortfolio;
                _client.Responses.AccountDownloadEnd -= accountDownloadEnd;
            }
        }
    }
}
