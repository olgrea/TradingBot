using System;
using System.Collections.Generic;
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

        void RequestBidAsk(Contract contract);
        Action<Contract, BidAsk> BidAskReceived { get; set; }
        void CancelBidAskRequest(Contract contract);

        void RequestBars(Contract contract, BarLength barLength);
        Dictionary<BarLength, Action<Contract, MarketData.Bar>> BarReceived { get; set; }
        void CancelBarsRequest(Contract contract, BarLength barLength);
        void CancelAllBarsRequest(Contract contract);

        void PlaceOrder(Contract contract, Order order);
        // TODO : remove TWS specific stuff
        void PlaceOrder(Contract contract, OrderChain chain, bool useTWSAttachedOrderFeature = false);
        void ModifyOrder(Contract contract, Order order);
        Action<Contract, Order, OrderState> OrderOpened { get; set; }
        Action<CommissionInfo> CommissionInfoReceived { get; set; }
        Action<Contract, OrderExecution> OrderExecuted { get; set; }
        Action<OrderStatus> OrderStatusChanged { get; set; }
        void CancelOrder(Order order);

        void RequestPnL(Contract contract);
        Action<PnL> PnLReceived { get; set; }
        void CancelPnLSubscription(Contract contract);

        void CancelPositionsSubscription();
        Action<Position> PositionReceived { get; set; }
        void RequestPositions();

        Action<ClientMessage> ClientMessageReceived { get; set; }
    }
}
