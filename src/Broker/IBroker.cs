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
        Dictionary<BarLength, Action<Contract, Bar>> BarReceived { get; set; }
        event Action<Contract, BidAsk> BidAskReceived;
        event Action<string, string, string, string> AccountValueUpdated;
        event Action<Contract, Order, OrderState> OrderOpened;
        event Action<OrderStatus> OrderStatusChanged;
        event Action<Contract, OrderExecution> OrderExecuted;
        event Action<CommissionInfo> CommissionInfoReceived;
        event Action<Position> PositionReceived;
        event Action<PnL> PnLReceived;
        IErrorHandler ErrorHandler { get; set; }

        Task<ConnectResult> ConnectAsync();
        Task<ConnectResult> ConnectAsync(CancellationToken token);
        Task<bool> DisconnectAsync();
        Task<Account> GetAccountAsync(string accountCode);
        void RequestAccountUpdates(string accountCode);
        void CancelAccountUpdates(string accountCode);
        Task<Contract> GetContractAsync(string symbol);
        Task<List<ContractDetails>> GetContractDetailsAsync(Contract contract);
        Task<BidAsk> GetLatestBidAskAsync(Contract contract);
        void RequestBidAskUpdates(Contract contract);
        void CancelBidAskUpdates(Contract contract);
        void RequestLastUpdates(Contract contract);
        void CancelLastUpdates(Contract contract);
        void RequestBarsUpdates(Contract contract);
        void CancelBarsUpdates(Contract contract);
        
        Task<OrderResult> PlaceOrderAsync(Contract contract, Order order);
        Task<OrderResult> PlaceOrderAsync(Contract contract, Order order, CancellationToken token);
        void PlaceOrder(Contract contract, Order order);
        Task<OrderStatus> CancelOrderAsync(int orderId);
        void CancelOrder(Order order);
        void CancelAllOrders();

        void RequestPnLUpdates(Contract contract);
        void CancelPnLUpdates(Contract contract);
        void RequestPositionsUpdates();
        void CancelPositionsUpdates();

        Task<IEnumerable<Bar>> GetHistoricalBarsAsync(Contract contract, BarLength barLength, DateTime endDateTime, int count);
        Task<IEnumerable<BidAsk>> GetHistoricalBidAsksAsync(Contract contract, DateTime time, int count);

        Task<int> GetNextValidOrderIdAsync();
        Task<DateTime> GetCurrentTimeAsync();
    }
}
