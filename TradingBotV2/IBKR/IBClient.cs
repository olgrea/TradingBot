using IBApi;

namespace TradingBotV2.IBKR
{
    internal class IBClient
    {
        public const int DefaultTWSPort = 7496;
        public const int DefaultIBGatewayPort = 4002;
        public const string DefaultIP = "127.0.0.1";

        EClientSocket _clientSocket;
        EReaderSignal _signal;
        EReader _reader;
        Task _processMsgTask;
        string _accountCode = null;

        public IBClient()
        {
            Responses = new IBResponses();
            _signal = new EReaderMonitorSignal();
            _clientSocket = new EClientSocket(Responses, _signal);
        }

        public IBResponses Responses { get; }

        // TODO : need to handle market data connection losses if the bot trades for multiple days. It seems to happen everyday at around 8pm

        public void Connect() => Connect(DefaultIP, DefaultTWSPort, new Random().Next());

        public void Connect(string host, int port, int clientId)
        {
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

        public void RequestContractDetails(int reqId, Contract contract)
        {
            _clientSocket.reqContractDetails(reqId, contract);
        }

        public void RequestPositionsUpdates()
        {
            _clientSocket.reqPositions();
        }

        public void CancelPositionsUpdates()
        {
            _clientSocket.cancelPositions();
        }

        public void RequestPnLUpdates(int reqId, int contractId)
        {
            _clientSocket.reqPnLSingle(reqId, _accountCode, "", contractId);
        }

        public void CancelPnLUpdates(int reqId)
        {
            _clientSocket.cancelPnLSingle(reqId);
        }

        public void RequestFiveSecondsBarUpdates(int reqId, Contract contract)
        {
            // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
            _clientSocket.reqRealTimeBars(reqId, contract, 5, "TRADES", true, null);
        }

        public void CancelFiveSecondsBarsUpdates(int reqId)
        {
            _clientSocket.cancelRealTimeBars(reqId);
        }

        public void RequestTickByTickData(int reqId, Contract contract, string tickType)
        {
            _clientSocket.reqTickByTickData(reqId, contract, tickType, 0, false);
        }

        public void CancelTickByTickData(int reqId)
        {
            _clientSocket.cancelTickByTickData(reqId);
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

        public void RequestHistoricalData(int reqId, Contract contract, string endDateTime, string durationStr, string barSizeStr, bool onlyRTH)
        {
            _clientSocket.reqHistoricalData(reqId, contract, endDateTime, durationStr, barSizeStr, "TRADES", Convert.ToInt32(onlyRTH), 1, false, null);
        }

        public void RequestHistoricalTicks(int reqId, Contract contract, string startDateTime, string endDateTime, int nbOfTicks, string whatToShow, bool onlyRTH, bool ignoreSize)
        {
            _clientSocket.reqHistoricalTicks(reqId, contract, startDateTime, endDateTime, nbOfTicks, whatToShow, Convert.ToInt32(onlyRTH), ignoreSize, null);
        }

        public void RequestCurrentTime()
        {
            _clientSocket.reqCurrentTime();
        }
    }
}
