using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client.Messages;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;

namespace TradingBot.Broker.Client
{
    internal interface IIBClient
    {
        IBCallbacks Callbacks { get; }
        Task<ConnectMessage> ConnectAsync(string host, int port, int clientId);
        Task<ConnectMessage> ConnectAsync(string host, int port, int clientId, CancellationToken token);
        Task<bool> DisconnectAsync();
        Task<int> GetNextValidOrderIdAsync();
        void RequestAccountUpdates(string accountCode);
        void CancelAccountUpdates(string accountCode);
        void RequestPositionsUpdates();
        void CancelPositionsUpdates();
        void RequestPnLUpdates(int reqId, int contractId);
        void CancelPnLUpdates(int contractId);
        void RequestFiveSecondsBarUpdates(int reqId, Contract contract);
        void CancelFiveSecondsBarsUpdates(int reqId);
        Task<List<ContractDetails>> GetContractDetailsAsync(int reqId, Contract contract);
        void RequestOpenOrders();
        void PlaceOrder(Contract contract, Order order);
        Task<OrderMessage> PlaceOrderAsync(Contract contract, Orders.Order order);
        void CancelOrder(int orderId);
        Task<OrderStatus> CancelOrderAsync(int orderId);
        void CancelAllOrders();
        Task<LinkedList<MarketData.Bar>> GetHistoricalDataAsync(int reqId, Contract contract, BarLength barLength, DateTime endDateTime, int count);
        void RequestHistoricalData(int reqId, Contract contract, string endDateTime, string durationStr, string barSizeStr, bool onlyRTH);
        Task<IEnumerable<BidAsk>> RequestHistoricalTicks(int reqId, Contract contract, DateTime time, int count);
        void RequestTickByTickData(int reqId, Contract contract, string tickType);
        void CancelTickByTickData(int reqId);
        Task<long> GetCurrentTimeAsync();
    }
}
