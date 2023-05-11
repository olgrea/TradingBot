using System.Collections.Concurrent;
using System.Diagnostics;
using IBApi;
using NLog;

namespace TradingBotV2.IBKR.Client
{
    internal class IBClient
    {
        internal class RequestIdsToContracts
        {
            public ConcurrentDictionary<int, Contract> FiveSecBars { get; set; } = new();
            public ConcurrentDictionary<int, Contract> AllLast { get; set; } = new();
            public ConcurrentDictionary<int, Contract> Last { get; set; } = new();
            public ConcurrentDictionary<int, Contract> BidAsk { get; set; } = new();
            public ConcurrentDictionary<int, Contract> MidPoint { get; set; } = new();
            public ConcurrentDictionary<int, Contract> Pnl { get; set; } = new();
        }

        internal class ContractsToRequestIds
        {
            public ConcurrentDictionary<Contract, int> FiveSecBars { get; set; } = new();
            public ConcurrentDictionary<Contract, int> AllLast { get; set; } = new();
            public ConcurrentDictionary<Contract, int> Last { get; set; } = new();
            public ConcurrentDictionary<Contract, int> BidAsk { get; set; } = new();
            public ConcurrentDictionary<Contract, int> MidPoint { get; set; } = new();
            public ConcurrentDictionary<Contract, int> Pnl { get; set; } = new();
        }

        public const int DefaultTWSPort = 7496;
        public const int DefaultIBGatewayPort = 4002;
        public const string DefaultIP = "127.0.0.1";

        ILogger? _logger;
        EClientSocket _clientSocket;
        EReaderSignal _signal;
        EReader? _reader;
        Task? _processMsgTask;

        int _clientId = -1;
        string _accountCode = string.Empty;
        int _nextValidId = -1;
        RequestIdsToContracts _requestIdsToContracts = new ();
        ContractsToRequestIds _contractsToRequestIds = new ();
        ContractsCache _contractsCache;

        public IBClient(ILogger? logger = null)
        {
            _logger = logger;
            _signal = new EReaderMonitorSignal();
            Responses = new IBResponses(_requestIdsToContracts, logger);
            _clientSocket = new EClientSocket(Responses, _signal);

            _contractsCache = new ContractsCache(this);

            Responses.NextValidId += new Action<int>(id =>
            {
                _nextValidId = id;
            });
            Responses.ManagedAccounts += new Action<IEnumerable<string>>(accList =>
            {
                _accountCode = accList.First();
            });
            Responses.ConnectAck += new Action(() =>
            {
                Debug.Assert(_clientId > 0);
            });
        }

        int NextValidId => _nextValidId++;

        public IBResponses Responses { get; }
        internal ContractsCache ContractsCache => _contractsCache;

        // TODO : need to handle market data connection losses if the bot trades for multiple days. It seems to happen everyday at around 8pm

        public void Connect() => Connect(DefaultIP, DefaultTWSPort, new Random().Next());

        public void Connect(string host, int port, int clientId)
        {
            _clientId = clientId;
            _clientSocket.eConnect(host, port, clientId);
            _reader = new EReader(_clientSocket, _signal);
            _reader.Start();
            _processMsgTask = Task.Factory.StartNew(ProcessMsg, TaskCreationOptions.LongRunning);
            _processMsgTask.ContinueWith(t => Responses.error(t.Exception ?? new Exception("Unknown EReader Thread exception")), TaskContinuationOptions.OnlyOnFaulted);
        }

        void ProcessMsg()
        {
            while (_clientSocket.IsConnected())
            {
                ArgumentNullException.ThrowIfNull(_reader);
                _signal.waitForSignal();
                _reader.processMsgs();
            }
        }

        internal bool IsConnected()
        {
            return _clientSocket.IsConnected();
        }

        public void Disconnect()
        {
            _clientSocket.eDisconnect();
        }

        public void RequestValidOrderIds()
        {
            _clientSocket.reqIds(-1); // param is deprecated
        }

        public void RequestManagedAccounts()
        {
            _clientSocket.reqManagedAccts();
        }

        // https://interactivebrokers.github.io/tws-api/account_updates.html
        // Updates are first sent instantly when reqAccountUpdates(true, code) is called and then, if no change in positions occured,
        // at a FIXED interval of three minutes (not 3 minutes after the first call).
        // IMPORTANT also : accountDownloadEnd() is ONLY called on the first reqAccountUpdates(true, code) call, NOT during updates...
        // So you don't have any reliable ways of knowing when an update has finished...
        public void RequestAccountUpdates(string accountCode)
        {
            _clientSocket.reqAccountUpdates(true, accountCode);
        }

        public void CancelAccountUpdates(string accountCode)
        {
            _clientSocket.reqAccountUpdates(false, accountCode);
        }

        public int RequestContractDetails(Contract contract)
        {
            int reqId = NextValidId;
            _clientSocket.reqContractDetails(reqId, contract);
            return reqId;
        }

        public void RequestPositionsUpdates()
        {
            _clientSocket.reqPositions();
        }

        public void CancelPositionsUpdates()
        {
            _clientSocket.cancelPositions();
        }

        public void RequestPnLUpdates(string ticker)
        {
            var contract = ContractsCache.Get(ticker);
            if (!_contractsToRequestIds.Pnl.TryGetValue(contract, out _))
            {
                int reqId = NextValidId;
                _requestIdsToContracts.Pnl[reqId] = contract;
                _contractsToRequestIds.Pnl[contract] = reqId;

                // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
                _clientSocket.reqPnLSingle(reqId, _accountCode, "", contract.ConId);
            }
        }

        public void CancelPnLUpdates(string ticker)
        {
            var contract = ContractsCache.Get(ticker);
            if (_contractsToRequestIds.Pnl.TryGetValue(contract, out int reqId))
            {
                _clientSocket.cancelPnLSingle(reqId);
                _requestIdsToContracts.Pnl.TryRemove(reqId, out _);
                _contractsToRequestIds.Pnl.TryRemove(contract, out _);
            }
        }

        public void RequestFiveSecondsBarUpdates(string ticker)
        {
            var contract = ContractsCache.Get(ticker);
            if (!_contractsToRequestIds.FiveSecBars.TryGetValue(contract, out _))
            {
                int reqId = NextValidId;
                _requestIdsToContracts.FiveSecBars[reqId] = contract;
                _contractsToRequestIds.FiveSecBars[contract] = reqId;

                // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
                _clientSocket.reqRealTimeBars(reqId, contract, 5, "TRADES", true, null);
            }
        }

        public void CancelFiveSecondsBarsUpdates(string ticker)
        {
            var contract = ContractsCache.Get(ticker);
            if (_contractsToRequestIds.FiveSecBars.TryGetValue(contract, out var reqId))
            {
                _clientSocket.cancelRealTimeBars(reqId);
                _requestIdsToContracts.FiveSecBars.TryRemove(reqId, out _);
                _contractsToRequestIds.FiveSecBars.TryRemove(contract, out _);
            }
        }

        public void RequestTickByTickData(string ticker, string tickType)
        {
            var contract = ContractsCache.Get(ticker);
            var dicts = ValidateTickType(tickType);
            var idToC = dicts.Item1;
            var cToIds = dicts.Item2;

            if (!cToIds.TryGetValue(contract, out _))
            {
                int reqId = NextValidId;
                idToC[reqId] = contract;
                cToIds[contract] = reqId;
                _clientSocket.reqTickByTickData(reqId, contract, tickType, 0, false);
            }
        }

        public void CancelTickByTickData(string ticker, string tickType)
        {
            var contract = ContractsCache.Get(ticker);
            var dicts = ValidateTickType(tickType);
            var idToC = dicts.Item1;
            var cToIds = dicts.Item2;

            if (cToIds.TryGetValue(contract, out int reqId))
            {
                _clientSocket.cancelTickByTickData(reqId);
                idToC.TryRemove(reqId, out _);
                cToIds.TryRemove(contract, out _);
            }
        }

        private (ConcurrentDictionary<int, Contract>, ConcurrentDictionary<Contract, int>) ValidateTickType(string tickType)
        {
            (ConcurrentDictionary<int, Contract>, ConcurrentDictionary<Contract, int>) dicts;
            switch (tickType)
            {
                case "Last":
                    dicts = (_requestIdsToContracts.Last, _contractsToRequestIds.Last);
                    break;
                case "AllLast":
                    dicts = (_requestIdsToContracts.AllLast, _contractsToRequestIds.AllLast);
                    break;
                case "BidAsk":
                    dicts = (_requestIdsToContracts.BidAsk, _contractsToRequestIds.BidAsk);
                    break;
                case "MidPoint":
                    dicts = (_requestIdsToContracts.MidPoint, _contractsToRequestIds.MidPoint);
                    break;
                default:
                    throw new ArgumentException($"Invalid tick type \"{tickType}\"");
            };
            return dicts;
        }

        public void RequestOpenOrders()
        {
            _clientSocket.reqAllOpenOrders();
        }

        public void PlaceOrder(Contract contract, Order order)
        {
            Debug.Assert(order.OrderId > 0);
            Debug.Assert(order.TotalQuantity > 0);
            _clientSocket.placeOrder(order.OrderId, contract, order);
        }

        public void CancelOrder(int orderId)
        {
            Debug.Assert(orderId > 0);
            _clientSocket.cancelOrder(orderId, string.Empty);
        }

        public void CancelAllOrders()
        {
            _clientSocket.reqGlobalCancel();
        }

        public int RequestHistoricalData(Contract contract, string endDateTime, string durationStr, string barSizeStr, string whatToShow, bool onlyRTH)
        {
            int reqId = NextValidId;
            _clientSocket.reqHistoricalData(reqId, contract, endDateTime, durationStr, barSizeStr, whatToShow, Convert.ToInt32(onlyRTH), 1, false, null);
            return reqId;
        }

        public int RequestHistoricalTicks(Contract contract, string startDateTime, string endDateTime, int nbOfTicks, string whatToShow, bool onlyRTH, bool ignoreSize)
        {
            int reqId = NextValidId;
            _clientSocket.reqHistoricalTicks(reqId, contract, startDateTime, endDateTime, nbOfTicks, whatToShow, Convert.ToInt32(onlyRTH), ignoreSize, null);
            return reqId;
        }

        public void CancelHistoricalData(int reqId)
        {
            _clientSocket.cancelHistoricalData(reqId);
        }

        public void RequestServerTime()
        {
            _clientSocket.reqCurrentTime();
        }
    }
}
