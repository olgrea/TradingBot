using TradingBotV2.Broker;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Broker.Orders;
using TradingBotV2.IBKR;

namespace TradingBotV2.Backtesting
{
    internal class Backtester : IBroker
    {
        bool _isConnected = false;
        Account _fakeAccount;

        public Backtester()
        {
            _fakeAccount = new Account()
            {
                Code = "FAKEACCOUNT123",
                CashBalances = new Dictionary<string, double>()
                {
                    { "BASE", 25000.00 },
                    { "USD", 25000.00 },
                },
                UnrealizedPnL = new Dictionary<string, double>()
                {
                    { "BASE", 0.00},
                    { "USD", 0.00 },
                },
                RealizedPnL = new Dictionary<string, double>()
                {
                    { "BASE", 0.00 },
                    { "USD", 0.00 },
                }
            };
        }

        public IMarketDataProvider MarketDataProvider => throw new NotImplementedException();

        public IOrderManager OrderManager => throw new NotImplementedException();

        public Task<string> ConnectAsync()
        {
            if (_isConnected)
                throw new ErrorMessage("Already connected");

            _isConnected = true;
            return Task.FromResult<string>("FAKEACCOUNT123");
        }

        public Task DisconnectAsync()
        {
            _isConnected = false;
            return Task.CompletedTask;
        }

        public Task<Account> GetAccountAsync(string accountCode)
        {
            return Task.FromResult(_fakeAccount);
        }
    }
}
