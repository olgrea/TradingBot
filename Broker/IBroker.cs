using System;
using System.Collections.Generic;
using System.Text;
using TradingBot.Broker.MarketData;

namespace TradingBot.Broker
{
    internal interface IBroker
    {
        void Connect();
        void Disconnect();
        Contract GetContract(string ticker);
        void RequestBidAsk(Contract contract, Action<Contract, BidAsk> callback);
        void RequestBars(Contract contract, Action<Contract, Bar> callback);
        void CancelBarsRequest(Contract contract);
    }
}
