using TradingBotV2.Broker;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.Orders;

namespace TradingBotV2.IBKR
{
    internal class IBBroker : IBroker
    {
        IBClient _ibClient;

        public IMarketDataProvider MarketDataProvider => throw new NotImplementedException();

        public IOrderManager OrderManager => throw new NotImplementedException();

        public Task<bool> ConnectAsync()
        {
            throw new NotImplementedException();
        }

        public Task<bool> DisconnectAsync()
        {
            throw new NotImplementedException();
        }

        public Task<Account> GetAccountAsync(string accountCode)
        {
            throw new NotImplementedException();
        }
    }
}
