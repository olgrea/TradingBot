using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TradingBotV2.Broker;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.Orders;

namespace TradingBotV2.Backtesting
{
    internal class Backtester : IBroker
    {
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
