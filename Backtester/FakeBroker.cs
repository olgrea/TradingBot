using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IBApi;
using TradingBot.Broker;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Utils;
using Bar = TradingBot.Broker.MarketData.Bar;
using Contract = TradingBot.Broker.Contract;
using Order = TradingBot.Broker.Orders.Order;
using OrderState = TradingBot.Broker.Orders.OrderState;

namespace Backtester
{
    internal class FakeBroker : IBroker
    {
        public Dictionary<BarLength, EventElement<Contract, Bar>> BarReceived { get; }

        public event Action<Contract, BidAsk> BidAskReceived;
        public event Action<Contract, Order, OrderState> OrderOpened;
        public event Action<OrderStatus> OrderStatusChanged;
        public event Action<Contract, OrderExecution> OrderExecuted;
        public event Action<CommissionInfo> CommissionInfoReceived;
        public event Action<Position> PositionReceived;
        public event Action<PnL> PnLReceived;
        public event Action<ClientMessage> ClientMessageReceived;
        public event Action<string, string, string> AccountValueUpdated;

        public FakeBroker(ILogger logger)
        {
            BarReceived = new Dictionary<BarLength, EventElement<Contract, Bar>>();
            foreach (BarLength barLength in Enum.GetValues(typeof(BarLength)).OfType<BarLength>())
            {
                BarReceived.Add(barLength, new EventElement<Contract, Bar>());
            }
        }

        public void CancelBarsRequest(Contract contract, BarLength barLength)
        {
            throw new NotImplementedException();
        }

        public void CancelBidAskRequest(Contract contract){}

        public void CancelOrder(Order order)
        {
            throw new NotImplementedException();
        }

        public void CancelPnLSubscription(Contract contract){}

        public void CancelPositionsSubscription(){}

        public void Connect() {}
        public void Disconnect() {}
        public Account GetAccount()
        {
            return new Account()
            {
                Code = "FAKEACCOUNT123",
                CashBalances = new Dictionary<string, double>()
                 {
                     {"USD", 10000 }
                 }
            };
        }

        public Contract GetContract(string ticker)
        {
            throw new NotImplementedException();
        }

        public List<Bar> GetPastBars(Contract contract, BarLength barLength, int count)
        {
            throw new NotImplementedException();
        }

        public void ModifyOrder(Contract contract, Order order)
        {
            throw new NotImplementedException();
        }

        public void PlaceOrder(Contract contract, Order order)
        {
            throw new NotImplementedException();
        }

        public void PlaceOrder(Contract contract, OrderChain chain, bool useTWSAttachedOrderFeature = false)
        {
            throw new NotImplementedException();
        }

        public void RequestBars(Contract contract, BarLength barLength)
        {
            throw new NotImplementedException();
        }

        public void RequestBidAsk(Contract contract) {}
        public void RequestPnL(Contract contract) {}
        public void RequestPositions(){}
    }
}
