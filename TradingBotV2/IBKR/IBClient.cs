using System.Diagnostics;
using IBApi;

namespace TradingBotV2.IBKR
{
    internal class IBClient
    {
        internal class RequestIdsToContracts
        {
            public Dictionary<int, Contract> FiveSecBars { get; set; } = new Dictionary<int, Contract>();
            public Dictionary<int, Contract> AllLast { get; set; } = new Dictionary<int, Contract>();
            public Dictionary<int, Contract> Last { get; set; } = new Dictionary<int, Contract>();
            public Dictionary<int, Contract> BidAsk { get; set; } = new Dictionary<int, Contract>();
            public Dictionary<int, Contract> MidPoint { get; set; } = new Dictionary<int, Contract>();
            public Dictionary<int, Contract> Pnl { get; set; } = new Dictionary<int, Contract>();
        }

        public const int DefaultTWSPort = 7496;
        public const int DefaultIBGatewayPort = 4002;
        public const string DefaultIP = "127.0.0.1";

        EClientSocket _clientSocket;
        EReaderSignal _signal;
        EReader? _reader;
        Task? _processMsgTask;

        int _clientId = -1;
        string _accountCode = string.Empty;
        int _nextValidId = -1;
        RequestIdsToContracts _requestIdsToContracts = new RequestIdsToContracts();
        
        ContractsCache _contractsCache;

        public IBClient()
        {
            _signal = new EReaderMonitorSignal();
            Responses = new IBResponses(_requestIdsToContracts);
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
            if (!_requestIdsToContracts.Pnl.ContainsValue(contract))
            {
                int reqId = NextValidId;
                _requestIdsToContracts.Pnl[reqId] = contract;

                // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
                _clientSocket.reqPnLSingle(reqId, _accountCode, "", contract.ConId);
            }
        }

        public void CancelPnLUpdates(string ticker)
        {
            var contract = ContractsCache.Get(ticker);
            if (_requestIdsToContracts.Pnl.ContainsValue(contract))
            {
                var reqId = _requestIdsToContracts.Pnl.First(kvp => kvp.Value == contract).Key;
                _requestIdsToContracts.Pnl.Remove(reqId);
                _clientSocket.cancelPnLSingle(reqId);
            }
        }

        public void RequestFiveSecondsBarUpdates(string ticker)
        {
            var contract = ContractsCache.Get(ticker);
            if(!_requestIdsToContracts.FiveSecBars.ContainsValue(contract))
            {
                int reqId = NextValidId;
                _requestIdsToContracts.FiveSecBars[reqId] = contract;

                // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
                _clientSocket.reqRealTimeBars(reqId, contract, 5, "TRADES", true, null);
            }
        }

        public void CancelFiveSecondsBarsUpdates(string ticker)
        {
            var contract = ContractsCache.Get(ticker);
            if (_requestIdsToContracts.FiveSecBars.ContainsValue(contract))
            {
                var reqId = _requestIdsToContracts.FiveSecBars.First(kvp => kvp.Value == contract).Key;
                _requestIdsToContracts.FiveSecBars.Remove(reqId);
                _clientSocket.cancelRealTimeBars(reqId);
            }
        }

        public void RequestTickByTickData(string ticker, string tickType)
        {
            var contract = ContractsCache.Get(ticker);
            var idToC = ValidateTickType(tickType);
            if (!idToC.ContainsValue(contract))
            {
                int reqId = NextValidId;
                idToC[reqId] = contract;
                _clientSocket.reqTickByTickData(reqId, contract, tickType, 0, false);
            }
        }

        public void CancelTickByTickData(string ticker, string tickType)
        {
            var contract = ContractsCache.Get(ticker);
            var idToC = ValidateTickType(tickType);
            if (idToC.ContainsValue(contract))
            {
                var reqId = idToC.First(kvp => kvp.Value == contract).Key;
                idToC.Remove(reqId);
                _clientSocket.cancelTickByTickData(reqId);
            }
        }

        private Dictionary<int, Contract> ValidateTickType(string tickType)
        {
            Dictionary<int, Contract> idToC;
            switch (tickType)
            {
                case "Last":
                    idToC = _requestIdsToContracts.Last;
                    break;
                case "AllLast":
                    idToC = _requestIdsToContracts.AllLast;
                    break;
                case "BidAsk":
                    idToC = _requestIdsToContracts.BidAsk;
                    break;
                case "MidPoint":
                    idToC = _requestIdsToContracts.MidPoint;
                    break;
                default:
                    throw new ArgumentException($"Invalid tick type \"{tickType}\"");
            };
            return idToC;
        }

        public void RequestOpenOrders()
        {
            _clientSocket.reqOpenOrders();
        }

        public void PlaceOrder(Contract contract, Order order)
        {
            Debug.Assert(order.OrderId > 0);
            _clientSocket.placeOrder(order.OrderId, contract, order);
        }

        public void CancelOrder(int orderId)
        {
            Debug.Assert(orderId > 0);
            _clientSocket.cancelOrder(orderId);
        }

        public void CancelAllOrders()
        {
            _clientSocket.reqGlobalCancel();
        }

        public int RequestHistoricalData(Contract contract, string endDateTime, string durationStr, string barSizeStr, bool onlyRTH)
        {
            int reqId = NextValidId;
            _clientSocket.reqHistoricalData(reqId, contract, endDateTime, durationStr, barSizeStr, "TRADES", Convert.ToInt32(onlyRTH), 1, false, null);
            return reqId;
        }

        public int RequestHistoricalTicks(Contract contract, string startDateTime, string endDateTime, int nbOfTicks, string whatToShow, bool onlyRTH, bool ignoreSize)
        {
            int reqId = NextValidId;
            _clientSocket.reqHistoricalTicks(reqId, contract, startDateTime, endDateTime, nbOfTicks, whatToShow, Convert.ToInt32(onlyRTH), ignoreSize, null);
            return reqId;
        }

        public void RequestCurrentTime()
        {
            _clientSocket.reqCurrentTime();
        }
    }
}
