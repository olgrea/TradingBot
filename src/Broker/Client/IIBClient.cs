using TradingBot.Broker.Orders;

namespace TradingBot.Broker.Client
{
    internal interface IIBClient
    {
        void Connect(string host, int port, int clientId);
        void Disconnect();
        void RequestValidOrderIds();
        void RequestAccount(string accountCode, bool receiveUpdates = true);
        void RequestPositions();
        void CancelPositions();
        void RequestPnL(int reqId, string accountCode, int contractId);
        void CancelPnL(int contractId);
        void RequestFiveSecondsBars(int reqId, Contract contract);
        void CancelFiveSecondsBarsRequest(int reqId);
        void RequestTickByTickData(int reqId, Contract contract, string tickType);
        void CancelTickByTickData(int reqId);
        void RequestContract(int reqId, Contract contract);
        void RequestOpenOrders();
        void PlaceOrder(Contract contract, Order order);
        void CancelOrder(int orderId);
        void CancelAllOrders();
        void RequestHistoricalData(int reqId, Contract contract, string endDateTime, string durationStr, string barSizeStr, bool onlyRTH);
    }
}
