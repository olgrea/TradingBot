using System.Diagnostics;
using System.Globalization;
using NLog;
using TradingBotV2.Broker;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.Contracts;
using TradingBotV2.Broker.MarketData.Providers;
using TradingBotV2.Broker.Orders;
using TradingBotV2.IBKR.Client;

namespace TradingBotV2.IBKR
{
    public class IBBroker : IBroker
    {
        int _clientId;
        int _port;
        IBClient _client;
        ILogger? _logger;
        Account? _account;
        DateTime? _lastTimeUpdate = null;
        HashSet<string> _pnlSubscriptions = new HashSet<string>();
        
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

            _client.Responses.Position += OnPositionReceived;
            _client.Responses.PositionEnd += OnPositionReceptionEnd;
            _client.Responses.PnlSingle += OnPnLReceived;

            _client.Responses.Error += e => ErrorOccured?.Invoke(e);
        }

        public event Action<AccountValue>? AccountValueUpdated;
        public event Action<Position>? PositionUpdated;
        public event Action<PnL>? PnLUpdated;
        public event Action<Exception>? ErrorOccured;

        internal IBClient Client => _client;
        public ILiveDataProvider LiveDataProvider { get; init; }
        public IHistoricalDataProvider HistoricalDataProvider { get; init; }
        public IOrderManager OrderManager { get; init; }

        int GetPort()
        {
            var ibGatewayProc = Process.GetProcessesByName("ibgateway").FirstOrDefault();
            if (ibGatewayProc != null)
            {
                _logger?.Trace($"ibgateway is running");
                return IBClient.DefaultIBGatewayPort;
            }

            var twsProc = Process.GetProcessesByName("tws").FirstOrDefault();
            if (twsProc != null)
            {
                _logger?.Trace($"TWS is running");
                return IBClient.DefaultTWSPort;
            }

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
            string accountCode = string.Empty;

            var nextValidId = new Action<int>(id =>
            {
                Debug.Assert(id > 0);
                nextId = id;
                if(nextId > 0 && !string.IsNullOrEmpty(accountCode))
                    tcs.TrySetResult(accountCode);
            });

            var managedAccounts = new Action<IEnumerable<string>>(accList =>
            {
                if (accList.Count() > 1)
                    tcs.SetException(new NotSupportedException("Only single account structures are supported."));

                accountCode = accList.First();
                if (nextId > 0 && !string.IsNullOrEmpty(accountCode))
                    tcs.TrySetResult(accountCode);
            });

            var error = new Action<ErrorMessage>(msg => tcs.TrySetException(msg));

            _client.Responses.NextValidId += nextValidId;
            _client.Responses.ManagedAccounts += managedAccounts;
            _client.Responses.Error += error;

            try
            {
                if(timeout.Milliseconds > 0)
                    _ = Task.Delay(timeout, token).ContinueWith(t => tcs.TrySetException(new TimeoutException($"{nameof(ConnectAsync)}")));

                _logger?.Debug($"Connecting client id {_clientId} to {IBClient.DefaultIP}:{_port}");
                _client.Connect(IBClient.DefaultIP, _port, _clientId);

                await tcs.Task;
                _account = new Account(tcs.Task.Result);
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

            var disconnect = new Action(() => tcs.TrySetResult());
            var error = new Action<ErrorMessage>(msg => tcs.TrySetException(msg));

            _client.Responses.ConnectionClosed += disconnect;
            _client.Responses.Error += error;
            try
            {
                _logger?.Debug($"Disconnecting from TWS");
                _client.Disconnect();
                await tcs.Task;
                _account = null;
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

        public async Task<Account> GetAccountAsync()
        {
            Debug.Assert(_account != null);
            await ValidateAccount();

            var tcs = new TaskCompletionSource<Account>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool accDownloaded = false;
            bool posDownloaded = false;

            Action<string> onAccountDownloadEnd = accCode =>
            {
                accDownloaded = true;
                if (accDownloaded && posDownloaded)
                    tcs.TrySetResult(_account);
            };
            Action positionEnd = () =>
            {
                posDownloaded = true;
                if (accDownloaded && posDownloaded)
                    tcs.TrySetResult(_account);
            };

            _client.Responses.AccountDownloadEnd += onAccountDownloadEnd;
            _client.Responses.PositionEnd += positionEnd;
            try
            {
                _logger?.Debug($"Retrieving Account {_account.Code}");
                RequestAccountUpdates();
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.AccountDownloadEnd -= onAccountDownloadEnd;
                _client.Responses.PositionEnd -= positionEnd;
            }
        }

        async Task ValidateAccount()
        {
            var accList = await GetManagedAccountsList();
            if (accList.Count() > 1)
            {
                throw new NotSupportedException("Multiple-account structures not supported.");
            }
        }

        // https://interactivebrokers.github.io/tws-api/account_updates.html
        public void RequestAccountUpdates()
        {
            Debug.Assert(_account != null);
            _logger?.Debug($"Requesting account {_account.Code} updates...");

            _client.CancelAccountUpdates(_account.Code); // we need to cancel it first
            _client.RequestAccountUpdates(_account.Code);
            _client.RequestPositionsUpdates();
        }

        public void CancelAccountUpdates()
        {
            Debug.Assert(_account != null);
            CancelPnLUpdates();
            _logger?.Debug($"Cancelling account {_account.Code} updates...");
            _client.CancelAccountUpdates(_account.Code);
            _client.CancelPositionsUpdates();
        }

        void OnAccountTimeUpdate(DateTime time)
        {
            Debug.Assert(_account != null);
            _account.Time = time;

            // The same timestamp is received multiple times...
            if(_lastTimeUpdate == null || _lastTimeUpdate < time)
            {
                _logger?.Debug($"OnAccountTimeUpdate : {time}");
                _lastTimeUpdate = time;
                AccountValueUpdated?.Invoke(new AccountValue(AccountValueKey.Time, time.ToString()));
            }
        }

        void OnAccountValueUpdate(IBApi.AccountValue accValue)
        {
            Debug.Assert(_account != null);
            string curr = !string.IsNullOrEmpty(accValue.Currency) ? $"currency={accValue.Currency}" : string.Empty;
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
                    _logger?.Trace($"OnAccountValueUpdate : key={accValue.Key}, value={accValue.Value} {curr}");
                    _account.CashBalances[accValue.Currency] = double.Parse(accValue.Value, CultureInfo.InvariantCulture);
                    AccountValueUpdated?.Invoke((AccountValue)accValue);
                    break;

                case "RealizedPnL":
                    _logger?.Trace($"OnAccountValueUpdate : key={accValue.Key}, value={accValue.Value} {curr}");
                    _account.RealizedPnL[accValue.Currency] = double.Parse(accValue.Value, CultureInfo.InvariantCulture);
                    AccountValueUpdated?.Invoke((AccountValue)accValue);
                    break;

                case "UnrealizedPnL":
                    _logger?.Trace($"OnAccountValueUpdate : key={accValue.Key}, value={accValue.Value} {curr}");
                    _account.UnrealizedPnL[accValue.Currency] = double.Parse(accValue.Value, CultureInfo.InvariantCulture);
                    AccountValueUpdated?.Invoke((AccountValue)accValue);
                    break;
                
                default:
                    //_logger?.Trace($"OnAccountValueUpdate : key={accValue.Key}, value={accValue.Value} {curr}");
                    break;
            }
        }

        void OnPortfolioUpdate(IBApi.Position ibPos)
        {
            Debug.Assert(_account != null);
            Position pos = (Position)ibPos;
            _logger?.Trace($"OnPortfolioUpdate : {pos}");
            _account.Positions[ibPos.Contract.Symbol] = pos;
        }

        void OnAccountDownloadEnd(string account)
        {
            Debug.Assert(_account != null);
            _logger?.Trace($"OnAccountDownloadEnd");
        }

        // NOTE : called from an ActionBlock 
        void OnPositionReceived(IBApi.Position ibPos)
        {
            Debug.Assert(_account != null);
            Contract c = (Contract)ibPos.Contract;
            Debug.Assert(c is Stock);

            Position pos = (Position)ibPos;

            string msg = $"Position received :  {pos}";
            if(pos.PositionAmount > 0)
                _logger?.Debug(msg);
            else
                _logger?.Trace(msg);

            if(pos.PositionAmount > 0 && !_pnlSubscriptions.Contains(pos.Ticker!))
            {
                RequestPnLUpdate(pos);
            }
            else if (pos.PositionAmount == 0 && _pnlSubscriptions.Contains(pos.Ticker!))
            {
                CancelPnLUpdate(pos.Ticker!);
            }

            _account.Positions[pos.Ticker!] = pos;
            PositionUpdated?.Invoke(pos);
        }

        void OnPositionReceptionEnd()
        {
            Debug.Assert(_account != null);
            _logger?.Trace($"OnPositionReceptionEnd");
        }

        void RequestPnLUpdate(Position pos)
        {
            if (pos.PositionAmount <= 0)
                return;

            _logger?.Trace($"Requesting live PnL updates for ticker {pos.Ticker}...");

            // PnL subscriptions are requested after receiving a position update. Since these updates are supplied
            // by the EReader thread, we start a Task in order to prevent a deadlock.
            // TODO : implement some kind of custom SynchronizationContext in order to prevent having to do this?
            Task.Run(() => _client.RequestPnLUpdates(pos.Ticker!));
            _pnlSubscriptions.Add(pos.Ticker!);
        }

        HashSet<string> _pnlTraces = new HashSet<string>();
        void OnPnLReceived(IBApi.PnL pnl)
        {
            if(!_pnlTraces.Contains(pnl.Ticker))
            {
                _pnlTraces.Add(pnl.Ticker);
                _logger?.Trace($"pnl received : {pnl.Ticker}");
            }

            PnLUpdated?.Invoke((PnL)pnl);
        }

        void CancelPnLUpdates()
        {
            Debug.Assert(_account != null);
            foreach (var ticker in _pnlSubscriptions)
                CancelPnLUpdate(ticker);
        }

        void CancelPnLUpdate(string ticker)
        {
            _logger?.Trace($"Cancelling live PnL updates for ticker {ticker}...");
            Task.Run(() => _client.CancelPnLUpdates(ticker));
            _pnlTraces.Remove(ticker);
            _pnlSubscriptions.Remove(ticker);
        }
    }
}
