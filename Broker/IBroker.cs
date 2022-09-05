using System;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;

namespace TradingBot.Broker
{
    public interface IBroker
    {
        void Connect();
        void Disconnect();
        Accounts.Account GetAccount();
        Contract GetContract(string ticker);

        event Action<Contract, BidAsk> BidAskReceived;
        event Action<Contract, MarketData.Bar> Bar5SecReceived;
        event Action<Contract, MarketData.Bar> Bar1MinReceived;
        event Action<Contract, Order, OrderState> OrderOpened;
        event Action<OrderStatus> OrderStatusChanged;
        event Action<Contract, OrderExecution> OrderExecuted;
        event Action<CommissionInfo> CommissionInfoReceived;
        event Action<Position> PositionReceived;
        event Action<PnL> PnLReceived;
        event Action<ClientMessage> ClientMessageReceived;

        void RequestBidAsk(Contract contract);
        void CancelBidAskRequest(Contract contract);

        void RequestBars(Contract contract, BarLength barLength);
        void CancelBarsRequest(Contract contract, BarLength barLength);
        void CancelAllBarsRequest(Contract contract);

        void PlaceOrder(Contract contract, Order order);
        // TODO : remove TWS specific stuff
        void PlaceOrder(Contract contract, OrderChain chain, bool useTWSAttachedOrderFeature = false);
        void ModifyOrder(Contract contract, Order order);
        void CancelOrder(Order order);

        void RequestPnL(Contract contract);
        void CancelPnLSubscription(Contract contract);

        void RequestPositions();
        void CancelPositionsSubscription();
    }
}
