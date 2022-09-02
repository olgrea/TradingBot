using System;
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
        void RequestBidAsk(Contract contract, Action<Contract, BidAsk> callback);
        void CancelBidAskRequest(Contract contract, Action<Contract, BidAsk> callback);
        void RequestBars(Contract contract, BarLength barLength, Action<Contract, Bar> callback);
        void CancelBarsRequest(Contract contract, BarLength barLength, Action<Contract, Bar> callback);
        void CancelAllBarsRequest(Contract contract);
        void PlaceOrder(Contract contract, Order order);
        void PlaceOrder(Contract contract, OrderChain chain, bool useTWSAttachedOrderFeature = false);
        void ModifyOrder(Contract contract, Order order);
        void CancelOrder(Order order);

    }
}
