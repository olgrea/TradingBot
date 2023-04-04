using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.Orders;

namespace TradingBotV2.Broker
{
    internal interface IBroker
    {
        public IMarketDataProvider MarketDataProvider { get; }
        public IOrderManager OrderManager { get; }

        public Task<string> ConnectAsync();
        public Task DisconnectAsync();
        public Task<Account> GetAccountAsync(string accountCode);

    }
}
