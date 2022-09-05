using System;
using System.Collections.Generic;
using TradingBot.Broker.Accounts;
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
        public Dictionary<BarLength, Action<Contract, MarketData.Bar>> BarReceived { get; set; }
        void CancelBarsRequest(Contract contract, BarLength barLength);
        void CancelAllBarsRequest(Contract contract);

        void PlaceOrder(Contract contract, Order order);
        // TODO : remove TWS specific stuff
        void PlaceOrder(Contract contract, OrderChain chain, bool useTWSAttachedOrderFeature = false);
        void ModifyOrder(Contract contract, Order order);
        void CancelOrder(Order order);

        Action<Position> PositionReceived { get; set; }
        Action<PnL> PnLReceived { get; set; }

    }
}
