using IBClient.Orders;
using IBClient.Contracts;

namespace IBClient.Backend
{
    internal interface IIBSocket
    {
        IBCallbacks Callbacks { get; }
        void Connect(string host, int port, int clientId);
        void Disconnect();
        void RequestValidOrderIds();
        void RequestAccountUpdates(string accountCode);
        void CancelAccountUpdates(string accountCode);
        void RequestContractDetails(int reqId, Contract contract);
        void RequestPositionsUpdates();
        void CancelPositionsUpdates();
        void RequestPnLUpdates(int reqId, int contractId);
        void CancelPnLUpdates(int contractId);
        void RequestFiveSecondsBarUpdates(int reqId, Contract contract);
        void CancelFiveSecondsBarsUpdates(int reqId);
        void RequestTickByTickData(int reqId, Contract contract, string tickType);
        void CancelTickByTickData(int reqId);
        void RequestOpenOrders();
        void PlaceOrder(Contract contract, Order order);
        void CancelOrder(int orderId);
        void CancelAllOrders();
        void RequestHistoricalData(int reqId, Contract contract, string endDateTime, string durationStr, string barSizeStr, bool onlyRTH);
        void RequestHistoricalTicks(int reqId, Contract contract, string startDateTime, string endDateTime, int nbOfTicks, string whatToShow, bool onlyRTH, bool ignoreSize);
        void RequestCurrentTime();
    }
}
