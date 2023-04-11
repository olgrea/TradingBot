using System.Diagnostics;
using IBApi;

namespace TradingBotV2.IBKR
{
    internal class IBClient
    {
        internal class ContractIdToRequestId
        {
            public Dictionary<int, int> FiveSecBars { get; set; } = new Dictionary<int, int>();
            public Dictionary<int, int> AllLast { get; set; } = new Dictionary<int, int>();
            public Dictionary<int, int> Last { get; set; } = new Dictionary<int, int>();
            public Dictionary<int, int> BidAsk { get; set; } = new Dictionary<int, int>();
            public Dictionary<int, int> MidPoint { get; set; } = new Dictionary<int, int>();
            public Dictionary<int, int> Pnl { get; set; } = new Dictionary<int, int>();
        }

        public const int DefaultTWSPort = 7496;
        public const int DefaultIBGatewayPort = 4002;
        public const string DefaultIP = "127.0.0.1";

        EClientSocket _clientSocket;
        EReaderSignal _signal;
        EReader _reader;
        Task _processMsgTask;

        int _clientId = -1;
        string _accountCode = null;
        int _nextValidId = -1;
        Dictionary<int, ContractIdToRequestId> _contractIdToRequestId = new Dictionary<int, ContractIdToRequestId>();
        ContractsCache _contractsCache;

        public IBClient()
        {
            _signal = new EReaderMonitorSignal();
            Responses = new IBResponses();
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
                Debug.Assert(!_contractIdToRequestId.ContainsKey(_clientId));
                _contractIdToRequestId[_clientId] = new ContractIdToRequestId();
            });
            Responses.ConnectionClosed += new Action(() =>
            {
                _contractIdToRequestId.Remove(_clientId);
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
            _processMsgTask = Task.Run(ProcessMsg);
        }

        void ProcessMsg()
        {
            while (_clientSocket.IsConnected())
            {
                _signal.waitForSignal();
                _reader.processMsgs();
            }
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

        public int RequestPnLUpdates(int contractId)
        {
            if (!_contractIdToRequestId[_clientId].Pnl.ContainsKey(contractId))
            {
                int reqId = NextValidId;
                _contractIdToRequestId[_clientId].Pnl[contractId] = reqId;
                _clientSocket.reqPnLSingle(reqId, _accountCode, "", contractId);
            }

            return _contractIdToRequestId[_clientId].Pnl[contractId];
        }

        public int CancelPnLUpdates(int contractId)
        {
            int reqId = -1;
            if (_contractIdToRequestId[_clientId].Pnl.ContainsKey(contractId))
            {
                reqId = _contractIdToRequestId[_clientId].Pnl[contractId];
                _contractIdToRequestId[_clientId].Pnl.Remove(contractId);
                _clientSocket.cancelPnLSingle(reqId);
            }

            return reqId;
        }

        public int RequestFiveSecondsBarUpdates(Contract contract)
        {
            if (!_contractIdToRequestId[_clientId].FiveSecBars.ContainsKey(contract.ConId))
            {
                int reqId = NextValidId;
                _contractIdToRequestId[_clientId].FiveSecBars[contract.ConId] = reqId;

                // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
                _clientSocket.reqRealTimeBars(reqId, contract, 5, "TRADES", true, null);
            }

            return _contractIdToRequestId[_clientId].FiveSecBars[contract.ConId];
        }

        public int CancelFiveSecondsBarsUpdates(Contract contract)
        {
            int reqId = -1;
            if (_contractIdToRequestId[_clientId].FiveSecBars.ContainsKey(contract.ConId))
            {
                reqId = _contractIdToRequestId[_clientId].FiveSecBars[contract.ConId];
                _contractIdToRequestId[_clientId].FiveSecBars.Remove(contract.ConId);
                _clientSocket.cancelRealTimeBars(reqId);
            }

            return reqId;
        }

        public int RequestTickByTickData(Contract contract, string tickType)
        {
            Dictionary<int, int> cToId = ValidateTickType(tickType);

            if (!cToId.ContainsKey(contract.ConId))
            {
                int reqId = NextValidId;
                cToId[contract.ConId] = reqId;
                _clientSocket.reqTickByTickData(reqId, contract, tickType, 0, false);
            }

            return cToId[contract.ConId];
        }

        public int CancelTickByTickData(Contract contract, string tickType)
        {
            int reqId = -1;
            Dictionary<int, int> cToId = ValidateTickType(tickType);
            if (cToId.ContainsKey(contract.ConId))
            {
                reqId = cToId[contract.ConId];
                cToId.Remove(contract.ConId);
                _clientSocket.cancelTickByTickData(reqId);
            }

            return reqId;
        }

        private Dictionary<int, int> ValidateTickType(string tickType)
        {
            Dictionary<int, int> cToId;
            switch (tickType)
            {
                case "Last":
                    cToId = _contractIdToRequestId[_clientId].Last;
                    break;
                case "AllLast":
                    cToId = _contractIdToRequestId[_clientId].AllLast;
                    break;
                case "BidAsk":
                    cToId = _contractIdToRequestId[_clientId].BidAsk;
                    break;
                case "MidPoint":
                    cToId = _contractIdToRequestId[_clientId].MidPoint;
                    break;
                default:
                    throw new ArgumentException($"Invalid tick type \"{tickType}\"");
            };
            return cToId;
        }

        public void RequestOpenOrders()
        {
            _clientSocket.reqOpenOrders();
        }

        public void PlaceOrder(Contract contract, Order order)
        {
            _clientSocket.placeOrder(order.OrderId, contract, order);
        }

        public void CancelOrder(int orderId)
        {
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
