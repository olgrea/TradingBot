using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using NLog;
using TradingBotV2.Broker;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Strategies;
using TradingBotV2.Strategies.TestStrategies;

[assembly: InternalsVisibleTo("TradingBotV2.Tests")]
namespace TradingBotV2
{
    public class Trader
    {
        ILogger _logger;
        IBroker _broker;
        Account? _account;
        List<IStrategy> _strategies = new();

        public Trader(IBroker broker, ILogger logger)
        {
            _broker = broker;
            _logger = logger;
            AddStrategy(new BollingerBandsStrategy(MarketDataUtils.MarketStartTime, MarketDataUtils.MarketEndTime, "GME", this));
        }

        internal ILogger Logger => _logger;
        internal IBroker Broker => _broker;
        internal Account Account => _account!;

        void AddStrategy(IStrategy newStrat)
        {
            foreach (IStrategy strat in _strategies)
            {
                if (AreOverlapping(strat, newStrat))
                    throw new InvalidOperationException($"Multiple strategies cannot overlap during a trading day.");
            }

            _strategies.Add(newStrat);
        }

        public async Task Start()
        {
            if (!_strategies.Any())
                throw new Exception("No strategies set for this trader");

            var accCode = await _broker.ConnectAsync();
            if (accCode != "DU5962304" && accCode != "FAKEACCOUNT123")
                throw new Exception($"Only paper trading and tests are allowed for now");

            _broker.AccountValueUpdated += OnAccountValueUpdated;
            _broker.PositionUpdated += OnPositionUpdated;
            _broker.PnLUpdated += OnPnLUpdated;
            _account = await _broker.GetAccountAsync();
            Debug.Assert(_account != null);

            if (!_account.CashBalances.ContainsKey("USD"))
                throw new Exception($"No USD cash funds in account {_account.Code}. This trader only trades in USD.");

            foreach(IStrategy strat in _strategies.OrderBy(s => s.StartTime))
            {
                await strat.Start();
            }
        }

        void OnAccountValueUpdated(AccountValue val)
        {
            // TODO : log
        }

        void OnPositionUpdated(Position pos)
        {
            
        }

        void OnPnLUpdated(PnL pnl)
        {
            
        }

        bool AreOverlapping(IStrategy s1, IStrategy s2)
        {
            return s1.StartTime < s2.EndTime || s2.StartTime < s1.EndTime;
        }
    }
}