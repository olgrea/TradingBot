using System.Diagnostics;
using System.Globalization;
using NLog;
using TradingBotV2.Broker;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.MarketData.Providers;
using TradingBotV2.Broker.Orders;

namespace TradingBotV2.IBKR
{
    public class IBBroker : IBroker
    {
        int _clientId;
        int _port;
        IBClient _client;
        ILogger? _logger;
        
        bool _accountValuesRequested = false;
        Account? _tmpAccount;

        public IBBroker(int clientId, ILogger? logger = null)
        {
            _port = GetPort();
            _clientId = clientId;
            _client = new IBClient();
            _logger = logger;

            LiveDataProvider = new IBLiveDataProvider(_client);
            HistoricalDataProvider = new IBHistoricalDataProvider(this, logger);
            OrderManager = new IBOrderManager(this, logger);

            _client.Responses.UpdateAccountTime += OnAccountTimeUpdate;
            _client.Responses.UpdateAccountValue += OnAccountValueUpdate;
            _client.Responses.UpdatePortfolio += OnPortfolioUpdate;
            _client.Responses.AccountDownloadEnd += OnAccountDownloadEnd;
        }

        public event Action<Account>? AccountUpdated;

        internal IBClient Client => _client;
        public ILiveDataProvider LiveDataProvider { get; init; }
        public IHistoricalDataProvider HistoricalDataProvider { get; init; }
        public IOrderManager OrderManager { get; init; }

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

        public bool IsConnected() => _client.IsConnected();

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
                _logger?.Trace($"ConnectAsync : next valid id {id}");
                Debug.Assert(id > 0);
                nextId = id;
                if(nextId > 0 && !string.IsNullOrEmpty(account))
                    tcs.TrySetResult(account);
            });

            var managedAccounts = new Action<IEnumerable<string>>(accList =>
            {
                if (accList.Count() > 1)
                    tcs.SetException(new NotSupportedException("Only single account structures are supported."));

                _logger?.Trace($"ConnectAsync : managedAccounts {accList} - set result");
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
            if (!_client.IsConnected())
                return;

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _logger?.Debug($"Disconnecting from TWS");

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

        public async Task<Account> GetAccountAsync(string accountCode)
        {
            await ValidateAccount(accountCode);

            var tcs = new TaskCompletionSource<Account>(TaskCreationOptions.RunContinuationsAsynchronously);
            var accountUpdated = new Action<Account>(account => 
            {
                if (account.Code == accountCode)
                    tcs.TrySetResult(account);
            });

            AccountUpdated += accountUpdated;
            try
            {
                RequestAccountUpdates(accountCode);
                return await tcs.Task;
            }
            finally
            {
                CancelAccountUpdates(accountCode);
                AccountUpdated -= accountUpdated;
            }
        }

        async Task ValidateAccount(string accountCode)
        {
            var accList = await GetManagedAccountsList();
            if (accList.Count() > 1)
            {
                throw new NotSupportedException("Multiple-account structures not supported.");
            }
            else if (accList.First() != accountCode)
            {
                throw new ArgumentException($"The account code \"{accountCode}\" doesn't exists.");
            }
        }

        public void RequestAccountUpdates(string account)
        {
            if (!_accountValuesRequested)
            {
                ValidateAccount(account).Wait();
                _tmpAccount = new Account(account);
                _accountValuesRequested = true;
                _client.RequestAccountUpdates(account);
            }
        }

        public void CancelAccountUpdates(string account)
        {
            if (_accountValuesRequested)
            {
                _tmpAccount = null;
                _accountValuesRequested = false;
                _client.CancelAccountUpdates(account);
            }
        }

        void OnAccountDownloadEnd(string account)
        {
            Debug.Assert(_tmpAccount != null);
            AccountUpdated?.Invoke(_tmpAccount);
        }

        void OnPortfolioUpdate(IBApi.Position pos)
        {
            Debug.Assert(_tmpAccount != null);
            _logger?.Trace($"GetAccountAsync updatePortfolio : {pos}");
            _tmpAccount.Positions[pos.Contract.Symbol] = (Position)pos;
        }

        void OnAccountValueUpdate(IBApi.AccountValue accValue)
        {
            Debug.Assert(_tmpAccount != null);
            _logger?.Trace($"GetAccountAsync updateAccountValue : key={accValue.Key}, value={accValue.Value}");
            switch (accValue.Key)
            {
                case "AccountReady":
                    if (!bool.Parse(accValue.Value))
                    {
                        string msg = "Account not available at the moment. The IB server is in the process of resetting. Values returned may not be accurate.";
                        _logger?.Warn(msg);
                    }
                    break;

                case "CashBalance":
                    _tmpAccount.CashBalances[accValue.Currency] = double.Parse(accValue.Value, CultureInfo.InvariantCulture);
                    break;

                case "RealizedPnL":
                    _tmpAccount.RealizedPnL[accValue.Currency] = double.Parse(accValue.Value, CultureInfo.InvariantCulture);
                    break;

                case "UnrealizedPnL":
                    _tmpAccount.UnrealizedPnL[accValue.Currency] = double.Parse(accValue.Value, CultureInfo.InvariantCulture);
                    break;
            }
        }

        void OnAccountTimeUpdate(TimeSpan time)
        {
            Debug.Assert(_tmpAccount != null);
            _logger?.Trace($"GetAccountAsync updateAccountTime : {time}");
            _tmpAccount.Time = time;
        }
    }
}
