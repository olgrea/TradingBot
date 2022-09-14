using System;
using System.Threading.Tasks;
using IBApi;
using TradingBot.Utils;

namespace TradingBot.Broker.Client
{
    internal class IBClient : IIBClient
    {
        EClientSocket _clientSocket;
        EReaderSignal _signal;
        EReader _reader;
        IBCallbacks _callbacks;
        ILogger _logger;
        Task _processMsgTask;

        public IBClient(ILogger logger)
        {
            _logger = logger;
            _callbacks = new IBCallbacks(logger);
            _signal = new EReaderMonitorSignal();
            _clientSocket = new EClientSocket(_callbacks, _signal);
        }

#if BACKTESTING
        public IBClient(IBCallbacks callbacks, ILogger logger)
        {
            _logger = logger;
            _callbacks = callbacks;
            _signal = new EReaderMonitorSignal();
            _clientSocket = new EClientSocket(_callbacks, _signal);
        }
#endif

        // TODO : really need to handle market data connection losses. It seems to happen everyday at around 8pm

        public void Connect(string host, int port, int clientId)
        {
            _clientSocket.eConnect(host, port, clientId);
            _reader = new EReader(_clientSocket, _signal);
            _reader.Start();
            _processMsgTask = Task.Run(ProcessMsg);
            _logger.LogDebug($"Reader started and is listening to messages from TWS");
        }

        public IBCallbacks Callbacks => _callbacks;

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
            _logger.LogDebug($"Disconnecting from TWS");
            _clientSocket.eDisconnect();
        }

        public void RequestValidOrderIds()
        {
            _logger.LogDebug($"Requesting next valid order ids");
            _clientSocket.reqIds(-1); // param is deprecated
        }

        public void RequestAccount(string accountCode, bool receiveUpdates = true)
        {
            _logger.LogDebug($"Requesting values from account {accountCode}");
            _clientSocket.reqAccountUpdates(receiveUpdates, accountCode);
        }

        public void RequestPositions()
        {
            _logger.LogDebug($"Requesting current positions");
            _clientSocket.reqPositions();
        }

        public void CancelPositions()
        {
            _logger.LogDebug($"Cancelling positions updates");
            _clientSocket.cancelPositions();
        }

        public void RequestPnL(int reqId, string accountCode, int contractId)
        {
            _logger.LogDebug($"Requesting PnL for contract id : {contractId}");
            _clientSocket.reqPnLSingle(reqId, accountCode, "", contractId);
        }

        public void CancelPnL(int contractId)
        {
            _logger.LogDebug($"Cancelling PnL for contract id : {contractId}");
            _clientSocket.cancelPnL(contractId);
        }

        public void RequestFiveSecondsBars(int reqId, Contract contract)
        {
            _logger.LogDebug($"Requesting 5 sec bars for {contract} (reqId={reqId})");
            // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
            _clientSocket.reqRealTimeBars(reqId, contract.ToIBApiContract(), 5, "TRADES", true, null);
        }

        public void CancelFiveSecondsBarsRequest(int reqId)
        {
            _logger.LogDebug($"Cancelling 5 sec bars for reqId={reqId}");
            _clientSocket.cancelRealTimeBars(reqId);
        }

        public void RequestTickByTickData(int reqId, Contract contract, string tickType)
        {
            _logger.LogDebug($"Requesting tick by tick data ({tickType}) for {contract} (reqId={reqId})");
            // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
            _clientSocket.reqTickByTickData(reqId, contract.ToIBApiContract(), tickType, 0, false);
        }

        public void CancelTickByTickData(int reqId)
        {
            _logger.LogDebug($"Cancelling tick by tick data for reqId={reqId}");
            _clientSocket.cancelTickByTickData(reqId);
        }

        public void RequestContract(int reqId, Contract contract)
        {
            _logger.LogDebug($"Requesting contract {contract} (reqId={reqId})");
            _clientSocket.reqContractDetails(reqId, contract.ToIBApiContract());
        }

        public void RequestOpenOrders()
        {
            _logger.LogDebug($"Requesting open orders");
            _clientSocket.reqOpenOrders();
        }

        public void PlaceOrder(Contract contract, Orders.Order order)
        {
            _logger.LogDebug($"Requesting order placement for {contract} : {order}");
            var ibo = order.ToIBApiOrder();
            _clientSocket.placeOrder(ibo.OrderId, contract.ToIBApiContract(), ibo);
        }

        public void CancelOrder(int orderId)
        {
            _logger.LogDebug($"Requesting order cancellation for order id : {orderId}");
            _clientSocket.cancelOrder(orderId);
        }

        public void CancelAllOrders()
        {
            _logger.LogDebug($"Requesting cancellation for all open orders");
            _clientSocket.reqGlobalCancel();
        }

        public void RequestHistoricalData(int reqId, Contract contract, string endDateTime, string durationStr, string barSizeStr, bool onlyRTH)
        {
            _logger.LogDebug($"Requesting historical data for {contract} :\nendDateTime={endDateTime}, durationStr={durationStr}, barSizeStr={barSizeStr}, onlyRTH={onlyRTH}");
            _clientSocket.reqHistoricalData(reqId, contract.ToIBApiContract(), endDateTime, durationStr, barSizeStr, "TRADES", Convert.ToInt32(onlyRTH), 1, false, null);
        }
    }
}
