using System.Diagnostics;
using System.Globalization;
using NLog;
using TradingBot.Broker;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Contracts;
using TradingBot.Broker.MarketData.Providers;
using TradingBot.Broker.Orders;
using TradingBot.IBKR.Client;

namespace TradingBot.IBKR
{
    public class IBBroker : IBroker
    {
        int _clientId;
        int _port;
        IBClient _client;
        ILogger? _logger;
        Account? _account;
        HashSet<string> _pnlSubscriptions = new HashSet<string>();
        
        public IBBroker(int clientId, ILogger? logger = null)
        {
            _port = GetPort();
            _clientId = clientId;

            logger ??= LogManager.GetLogger($"IBBroker");

            _client = new IBClient(logger);
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
            _client.Responses.MessageReceived += m => MessageReceived?.Invoke(m);
        }

        public event Action<AccountValue>? AccountValueUpdated;
        public event Action<Position>? PositionUpdated;
        public event Action<PnL>? PnLUpdated;
        public event Action<Exception>? ErrorOccured;
        public event Action<Message>? MessageReceived;

        internal IBClient Client => _client;
        public ILiveDataProvider LiveDataProvider { get; init; }
        public IHistoricalDataProvider HistoricalDataProvider { get; init; }
        public IOrderManager OrderManager { get; init; }

        int GetPort()
        {
            var ibGatewayProc = Process.GetProcessesByName("ibgateway").FirstOrDefault();
            if (ibGatewayProc != null)
            {
                _logger?.Debug($"ibgateway is running");
                return IBClient.DefaultIBGatewayPort;
            }

            var twsProc = Process.GetProcessesByName("tws").FirstOrDefault();
            if (twsProc != null)
            {
                _logger?.Debug($"TWS is running");
                return IBClient.DefaultTWSPort;
            }

            throw new ArgumentException("Neither TWS Workstation or IB Gateway is running.");
        }

        public bool IsConnected() => _client.IsConnected();

        public async Task<string> ConnectAsync()
        {
            return await ConnectAsync(TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 5000), CancellationToken.None);
        }

        public async Task<string> ConnectAsync(CancellationToken token)
        {
            return await ConnectAsync(TimeSpan.FromMilliseconds(Debugger.IsAttached ? -1 : 5000), token);
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

            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

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
                _logger?.Debug($"Connected. Account Code : {_account.Code}");
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
            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _client.Responses.ConnectionClosed += disconnect;
            _client.Responses.Error += error;
            try
            {
                _logger?.Debug($"Disconnecting from TWS");
                _client.Disconnect();
                await tcs.Task;
                _logger?.Debug($"Disconnected.");
                _account = null;
            }
            finally
            {
                _client.Responses.ConnectionClosed -= disconnect;
                _client.Responses.Error -= error;
            }
        }
        
        async Task<IEnumerable<string>> GetManagedAccountsList(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<IEnumerable<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            var managedAccount = new Action<IEnumerable<string>>(list => tcs.SetResult(list));
            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _client.Responses.ManagedAccounts += managedAccount;
            try
            {
                _logger?.Debug($"Requesting managed account list...");
                _client.RequestManagedAccounts();
                var result = await tcs.Task;
                _logger?.Debug($"ManagedAccountlist received.");
                return result;
            }
            finally
            {
                _client.Responses.ManagedAccounts -= managedAccount;
                _client.Responses.Error -= error;
            }
        }

        public async Task<Account> GetAccountAsync() => await GetAccountAsync(CancellationToken.None);
        public async Task<Account> GetAccountAsync(CancellationToken token)
        {
            Debug.Assert(_account != null);
            await ValidateAccount(token);

            var tcs = new TaskCompletionSource<Account>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

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
                var result = await tcs.Task;
                _logger?.Debug($"Account received");
                return result;
            }
            finally
            {
                _client.Responses.AccountDownloadEnd -= onAccountDownloadEnd;
                _client.Responses.PositionEnd -= positionEnd;
            }
        }

        async Task ValidateAccount(CancellationToken token)
        {
            var accList = await GetManagedAccountsList(token);
            if (accList.Count() > 1)
            {
                throw new NotSupportedException("Multiple-account structures not supported.");
            }
        }

        // https://interactivebrokers.github.io/tws-api/account_updates.html
        public void RequestAccountUpdates()
        {
            Debug.Assert(_account != null);
            _logger?.Debug($"Requesting account updates for {_account.Code}...");


            // TODO : Handle connection lost
            // Market data farm connection is OK:cashfarm(code = 2104)
            // Market data farm connection is broken:cashfarm(code = 2103)
            _client.CancelAccountUpdates(_account.Code); // we need to cancel it first
            _client.RequestAccountUpdates(_account.Code);
            _client.RequestPositionsUpdates();
        }

        public void CancelAccountUpdates()
        {
            Debug.Assert(_account != null);
            CancelPnLUpdates();
            _logger?.Debug($"Cancelling account updates for {_account.Code}...");
            _client.CancelAccountUpdates(_account.Code);
            _client.CancelPositionsUpdates();
        }

        void OnAccountTimeUpdate(DateTime time)
        {
            Debug.Assert(_account != null);
            _account.Time = time;

            _logger?.Debug($"OnAccountTimeUpdate : {time}");
            AccountValueUpdated?.Invoke(new AccountValue(AccountValueKey.Time, time.ToString()));
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
                    _account.CashBalances[accValue.Currency] = double.Parse(accValue.Value, CultureInfo.InvariantCulture);
                    AccountValueUpdated?.Invoke((AccountValue)accValue);
                    break;

                case "RealizedPnL":
                    _account.RealizedPnL[accValue.Currency] = double.Parse(accValue.Value, CultureInfo.InvariantCulture);
                    AccountValueUpdated?.Invoke((AccountValue)accValue);
                    break;

                case "UnrealizedPnL":
                    _account.UnrealizedPnL[accValue.Currency] = double.Parse(accValue.Value, CultureInfo.InvariantCulture);
                    AccountValueUpdated?.Invoke((AccountValue)accValue);
                    break;
                
                default:
                    break;
            }
        }

        void OnPortfolioUpdate(IBApi.Position ibPos)
        {
            Debug.Assert(_account != null);
            Position pos = (Position)ibPos;
            _account.Positions[ibPos.Contract.Symbol] = pos;
        }

        void OnAccountDownloadEnd(string account)
        {
            Debug.Assert(_account != null);
        }

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
        }

        void RequestPnLUpdate(Position pos)
        {
            if (pos.PositionAmount <= 0)
                return;

            _logger?.Debug($"Requesting live PnL updates for ticker {pos.Ticker}...");

            // PnL subscriptions are requested after receiving a position update. These updates are supplied
            // by the EReader thread so we start a Task in order to prevent a deadlock.
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
            _logger?.Debug($"Cancelling live PnL updates for ticker {ticker}...");
            Task.Run(() => _client.CancelPnLUpdates(ticker));
            _pnlTraces.Remove(ticker);
            _pnlSubscriptions.Remove(ticker);
        }

        public async Task<DateTime> GetServerTimeAsync() => await GetServerTimeAsync(CancellationToken.None);
        public async Task<DateTime> GetServerTimeAsync(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously);
            var currentTime = new Action<long>(time =>
            {
                DateTime result = DateTimeOffset.FromUnixTimeSeconds(time).DateTime.ToLocalTime();
                tcs.TrySetResult(result);
            });
            var error = new Action<ErrorMessageException>(e => tcs.TrySetException(e));

            const int MaxRetries = 3;
            int nbRetries = 0;
            TimeSpan timeout = TimeSpan.FromSeconds(1);

            try
            {
                _client.Responses.CurrentTime += currentTime;
                _client.Responses.Error += error;

                // Server time is not received when multiple quick calls are made.
                // So we just retry.
                while (nbRetries < MaxRetries && !tcs.Task.IsCompleted)
                {
                    try
                    {
                        _logger?.Debug($"Requesting current server time...");
                        _client.RequestServerTime();
                        DateTime result = await tcs.Task.WaitAsync(timeout, token);
                        _logger?.Debug($"Server time : {result}");
                        return result;
                    }
                    catch (TimeoutException)
                    {
                        if (nbRetries < MaxRetries)
                        {
                            nbRetries++;
                            _logger?.Debug($"Timeout. Retrying... ({nbRetries}/{MaxRetries})");
                        }
                        else
                            throw;
                    }
                }

                return tcs.Task.Result;
            }
            finally
            {
                _client.Responses.CurrentTime -= currentTime;
                _client.Responses.Error -= error;
            }
        }
    }
}
