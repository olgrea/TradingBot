using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Utils;

[assembly: InternalsVisibleTo("Backtester")]
namespace TradingBot.Broker
{
    internal interface IBroker
    {
        void Connect();
        void Disconnect();
        Accounts.Account GetAccount();
        void CancelAccountUpdates(string account);
        Contract GetContract(string ticker);

        event Action<Contract, BidAsk> BidAskReceived;
        event Action<string, string, string> AccountValueUpdated;
        event Action<Contract, Bar> Bar5SecReceived;
        event Action<Contract, Bar> Bar1MinReceived;
        event Action<Order, OrderStatus> OrderUpdated;
        event Action<OrderExecution, CommissionInfo> OrderExecuted;
        event Action<Position> PositionReceived;
        event Action<PnL> PnLReceived;
        IErrorHandler ErrorHandler { get; set; }

        void RequestBidAsk(Contract contract);
        void CancelBidAskRequest(Contract contract);

        void RequestBars(Contract contract, BarLength barLength);
        void CancelBarsRequest(Contract contract, BarLength barLength);
        void SubscribeToBars(BarLength barLength, Action<Contract, Bar> callback);
        void UnsubscribeToBars(BarLength barLength, Action<Contract, Bar> callback);


        void PlaceOrder(Contract contract, Order order);
        void PlaceOrder(Contract contract, OrderChain chain);
        void ModifyOrder(Contract contract, Order order);
        void CancelOrder(Order order);
        void CancelAllOrders();
        bool HasBeenRequested(Order order);
        bool HasBeenOpened(Order order);
        bool IsExecuted(Order order);

        void RequestPnL(Contract contract);
        void CancelPnLSubscription(Contract contract);

        void RequestPositions();
        void CancelPositionsSubscription();
        IEnumerable<Bar> GetPastBars(Contract contract, BarLength barLength, int count);
        IEnumerable<BidAsk> GetPastBidAsks(Contract contract, DateTime time, int count);
        DateTime GetCurrentTime();
    }
}
