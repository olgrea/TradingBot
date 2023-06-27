using System.Collections.Concurrent;
using System.Diagnostics;
using IBApi;
using NLog;

namespace TradingBot.IBKR.Client
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
        EClientSocket _socket;
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
            _socket = new EClientSocket(Responses, _signal);

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
        internal ILogger? Logger => _logger;

        public void Connect() => Connect(DefaultIP, DefaultTWSPort, new Random().Next());

        public void Connect(string host, int port, int clientId)
        {
            _clientId = clientId;
            _socket.eConnect(host, port, clientId);
            _reader = new EReader(_socket, _signal);
            _reader.Start();
            _processMsgTask = Task.Factory.StartNew(ProcessMsg, TaskCreationOptions.LongRunning);
            _processMsgTask.ContinueWith(t =>
            {
                Exception e = t.Exception ?? new Exception("Unknown EReader Thread exception");
                _logger?.Fatal(e, $"EReader Thread failure.");
                Responses.error(e);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        void ProcessMsg()
        {
            while (_socket.IsConnected())
            {
                ArgumentNullException.ThrowIfNull(_reader);
                _signal.waitForSignal();
                _logger?.Trace("EReader Thread : process message.");
                _reader.processMsgs();
            }
        }

        internal bool IsConnected()
        {
            _logger?.Trace($"{nameof(IsConnected)}");
            return _socket.IsConnected();
        }

        public void Disconnect()
        {
            _logger?.Trace($"{nameof(Disconnect)}");
            _socket.eDisconnect();
        }

        public void RequestValidOrderIds()
        {
            _logger?.Trace($"{nameof(RequestValidOrderIds)}");
            _socket.reqIds(-1); // param is deprecated
        }

        public void RequestManagedAccounts()
        {
            _logger?.Trace($"{nameof(RequestManagedAccounts)}");
            _socket.reqManagedAccts();
        }

        // https://interactivebrokers.github.io/tws-api/account_updates.html
        // Updates are first sent instantly when reqAccountUpdates(true, code) is called and then, if no change in positions occured,
        // at a FIXED interval of 3 minutes (not 3 minutes after the first call).
        // IMPORTANT NOTES :
        // - IBResponses.accountDownloadEnd() is ONLY called on the first reqAccountUpdates(true, code) call, NOT during updates... So you don't
        //   have any reliable ways of knowing when an update has finished...
        // - The precision of timestamps received by IBResponses.updateAccountTime() is minutes
        // - IBResponses.updateAccountTime() can receive timestamps that can go as far as 6 minutes behind...
        public void RequestAccountUpdates(string accountCode)
        {
            _logger?.Trace($"{nameof(_socket.reqAccountUpdates)}(true, {accountCode})");
            _socket.reqAccountUpdates(true, accountCode);
        }

        public void CancelAccountUpdates(string accountCode)
        {
            _logger?.Trace($"{nameof(_socket.reqAccountUpdates)}(false, {accountCode})");
            _socket.reqAccountUpdates(false, accountCode);
        }

        public int RequestContractDetails(Contract contract)
        {
            int reqId = NextValidId;
            _logger?.Trace($"{nameof(_socket.reqContractDetails)}(reqId : {reqId}, ConId : {contract.ConId})");
            _socket.reqContractDetails(reqId, contract);
            return reqId;
        }

        public void RequestPositionsUpdates()
        {
            _logger?.Trace($"{nameof(_socket.reqPositions)}");
            _socket.reqPositions();
        }

        public void CancelPositionsUpdates()
        {
            _logger?.Trace($"{nameof(_socket.cancelPositions)}");
            _socket.cancelPositions();
        }

        public void RequestPnLUpdates(string ticker)
        {
            var contract = ContractsCache.Get(ticker);
            if (!_contractsToRequestIds.Pnl.TryGetValue(contract, out _))
            {
                int reqId = NextValidId;
                _requestIdsToContracts.Pnl[reqId] = contract;
                _contractsToRequestIds.Pnl[contract] = reqId;

                _logger?.Trace($"{nameof(_socket.reqPnLSingle)}(reqId : {reqId}, conId : {contract.ConId})");
                _socket.reqPnLSingle(reqId, _accountCode, "", contract.ConId);
            }
        }

        public void CancelPnLUpdates(string ticker)
        {
            var contract = ContractsCache.Get(ticker);
            if (_contractsToRequestIds.Pnl.TryGetValue(contract, out int reqId))
            {
                _logger?.Trace($"{nameof(_socket.cancelPnLSingle)}(reqId : {reqId})");
                _socket.cancelPnLSingle(reqId);

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

                _logger?.Trace($"{nameof(_socket.reqRealTimeBars)}(reqId : {reqId}, conId : {contract.ConId})");
                _socket.reqRealTimeBars(reqId, contract, 5, "TRADES", true, null);
            }
        }

        public void CancelFiveSecondsBarsUpdates(string ticker)
        {
            var contract = ContractsCache.Get(ticker);
            if (_contractsToRequestIds.FiveSecBars.TryGetValue(contract, out var reqId))
            {
                _logger?.Trace($"{nameof(_socket.cancelRealTimeBars)}(reqId : {reqId})");
                _socket.cancelRealTimeBars(reqId);
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

                _logger?.Trace($"{nameof(_socket.reqTickByTickData)}(reqId : {reqId}, conId : {contract.ConId}, tickType:{tickType})");
                _socket.reqTickByTickData(reqId, contract, tickType, 0, false);
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
                _logger?.Trace($"{nameof(_socket.cancelTickByTickData)}(reqId : {reqId})");
                _socket.cancelTickByTickData(reqId);
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
            _logger?.Trace($"{nameof(_socket.reqAllOpenOrders)}");
            _socket.reqAllOpenOrders();
        }

        public void PlaceOrder(Contract contract, Order order)
        {
            Debug.Assert(order.OrderId > 0);
            Debug.Assert(order.TotalQuantity > 0);

            foreach(var cond in order.Conditions.OfType<ContractCondition>())
            {
                cond.ConId = contract.ConId;
                cond.Exchange = contract.Exchange;
            }

            _logger?.Trace($"{nameof(_socket.placeOrder)}(orderId: {order.OrderId} conId: {contract.ConId})");
            _socket.placeOrder(order.OrderId, contract, order);
        }

        public void CancelOrder(int orderId)
        {
            Debug.Assert(orderId > 0);
            _logger?.Trace($"{nameof(_socket.cancelOrder)}(orderId: {orderId})");
            _socket.cancelOrder(orderId, string.Empty);
        }

        public void CancelAllOrders()
        {
            _logger?.Trace($"{nameof(_socket.reqGlobalCancel)}");
            _socket.reqGlobalCancel();
        }

        public int RequestHistoricalData(Contract contract, string endDateTime, string durationStr, string barSizeStr, string whatToShow, bool onlyRTH)
        {
            int reqId = NextValidId;
            _logger?.Trace($"{nameof(_socket.reqHistoricalData)}(reqId: {reqId}, conId: {contract.ConId}, endDateTime: {endDateTime}, durationStr: {durationStr}, barSize: {barSizeStr}, whatToShow: {whatToShow})");
            _socket.reqHistoricalData(reqId, contract, endDateTime, durationStr, barSizeStr, whatToShow, Convert.ToInt32(onlyRTH), 1, false, null);
            return reqId;
        }

        public int RequestHistoricalTicks(Contract contract, string startDateTime, string endDateTime, int nbOfTicks, string whatToShow, bool onlyRTH, bool ignoreSize)
        {
            int reqId = NextValidId;
            _logger?.Trace($"{nameof(_socket.reqHistoricalTicks)}(reqId: {reqId}, conId: {contract.ConId}, startDateTime: {startDateTime}, endDateTime: {endDateTime}, nbOfTicks: {nbOfTicks}, whatToShow: {whatToShow})");
            _socket.reqHistoricalTicks(reqId, contract, startDateTime, endDateTime, nbOfTicks, whatToShow, Convert.ToInt32(onlyRTH), ignoreSize, null);
            return reqId;
        }

        public void CancelHistoricalData(int reqId)
        {
            _logger?.Trace($"{nameof(_socket.cancelHistoricalData)}(reqId: {reqId})");
            _socket.cancelHistoricalData(reqId);
        }

        public void RequestServerTime()
        {
            _logger?.Trace($"{nameof(_socket.reqCurrentTime)}");
            _socket.reqCurrentTime();
        }
    }
}
