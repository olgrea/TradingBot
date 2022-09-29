using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;

namespace TradingBot.Broker.Client
{
    internal interface IIBClient
    {
        IBCallbacks Callbacks { get; }
        Task<bool> ConnectAsync(string host, int port, int clientId);
        void Connect(string host, int port, int clientId);
        void Disconnect();
        void RequestValidOrderIds();
        Task<Account> GetAccountAsync();
        void RequestAccount(string accountCode, bool receiveUpdates = true);
        void RequestPositions();
        void CancelPositions();
        void RequestPnL(int reqId, int contractId);
        void CancelPnL(int contractId);
        void RequestFiveSecondsBars(int reqId, Contract contract);
        void CancelFiveSecondsBarsRequest(int reqId);
        Task<List<Contract>> GetContractsAsync(int reqId, Contract contract);
        void RequestContract(int reqId, Contract contract);
        void RequestOpenOrders();
        void PlaceOrder(Contract contract, Order order);
        void CancelOrder(int orderId);
        void CancelAllOrders();
        Task<LinkedList<MarketData.Bar>> GetHistoricalDataAsync(int reqId, Contract contract, BarLength barLength, DateTime endDateTime, int count);
        void RequestHistoricalData(int reqId, Contract contract, string endDateTime, string durationStr, string barSizeStr, bool onlyRTH);
        Task<IEnumerable<BidAsk>> RequestHistoricalTicks(int reqId, Contract contract, DateTime time, int count);
        void RequestTickByTickData(int reqId, Contract contract, string tickType);
        void CancelTickByTickData(int reqId);
    }
}
