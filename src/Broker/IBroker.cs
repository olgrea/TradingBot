using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.Client.Messages;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Indicators;
using TradingBot.Utils;

[assembly: InternalsVisibleTo("Backtester")]
namespace TradingBot.Broker
{
    internal interface IBroker
    {
        event Action<Contract, BidAsk> BidAskReceived;
        event Action<string, string, string, string> AccountValueUpdated;
        event Action<Order, OrderStatus> OrderUpdated;
        event Action<OrderExecution, CommissionInfo> OrderExecuted;
        event Action<Position> PositionReceived;
        event Action<PnL> PnLReceived;
        IErrorHandler ErrorHandler { get; set; }

        Task<ConnectMessage> ConnectAsync();
        Task<ConnectMessage> ConnectAsync(CancellationToken token);
        Task<bool> DisconnectAsync();
        Task<Account> GetAccountAsync(string accountCode);
        void RequestAccountUpdates(string accountCode);
        void CancelAccountUpdates(string accountCode);
        Task<Contract> GetContractAsync(string symbol);
        Task<List<ContractDetails>> GetContractDetailsAsync(Contract contract);
        void RequestBidAskUpdates(Contract contract);
        void CancelBidAskUpdates(Contract contract);
        void RequestBarsUpdates(Contract contract, BarLength barLength);
        void CancelBarsUpdates(Contract contract, BarLength barLength);
        void SubscribeToBarUpdateEvent(BarLength barLength, Action<Contract, Bar> callback);
        void UnsubscribeToBarUpdateEvent(BarLength barLength, Action<Contract, Bar> callback);
        
        Task<OrderMessage> PlaceOrderAsync(Contract contract, Orders.Order order);
        void PlaceOrder(Contract contract, Order order);
        void PlaceOrder(Contract contract, OrderChain chain);
        void ModifyOrder(Contract contract, Order order);
        Task<OrderStatus> CancelOrderAsync(int orderId);
        void CancelOrder(Order order);
        void CancelAllOrders();
        bool HasBeenRequested(Order order);
        bool HasBeenOpened(Order order);
        bool IsCancelled(Order order);
        bool IsExecuted(Order order, out OrderExecution orderExecution);

        void RequestPnLUpdates(Contract contract);
        void CancelPnLUpdates(Contract contract);
        void RequestPositionsUpdates();
        void CancelPositionsUpdates();

        Task<LinkedList<MarketData.Bar>> GetHistoricalDataAsync(Contract contract, BarLength barLength, DateTime endDateTime, int count);
        Task<IEnumerable<BidAsk>> RequestHistoricalTicks(Contract contract, DateTime time, int count);

        Task<int> GetNextValidOrderIdAsync();
        Task<DateTime> GetCurrentTimeAsync();

        void InitIndicators(Contract contract, IEnumerable<IIndicator> indicators);
    }
}
