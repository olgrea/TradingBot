using System.Diagnostics;
using System.Runtime.CompilerServices;
using NLog;
using TradingBotV2.Broker;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.Broker.Orders;
using TradingBotV2.Strategies;
using TradingBotV2.Strategies.TestStrategies;
using TradingBotV2.Utils;

[assembly: InternalsVisibleTo("TradingBotV2.Tests")]
namespace TradingBotV2
{
    // TODO : read record doc again
    public record struct Trade(OrderAction Action, double Qty, string Ticker, double Price, double Commission, DateTime Time);

    public class TradeResults
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public List<Trade> Trades { get; set; } = new();
    }

    public class Trader
    {
        ILogger? _logger;
        IBroker _broker;
        Account? _account;
        List<IStrategy> _strategies = new();
        TradeResults _results = new();

        public Trader(IBroker broker, ILogger? logger = null)
        {
            _broker = broker;
            _logger = logger;
        }

        internal ILogger? Logger => _logger;
        internal IBroker Broker => _broker;
        internal Account Account => _account!;

        public void AddStrategy(IStrategy newStrat)
        {
            foreach (IStrategy strat in _strategies)
            {
                if (AreOverlapping(strat, newStrat))
                    throw new InvalidOperationException($"Multiple strategies cannot overlap during a trading day.");
            }

            _strategies.Add(newStrat);
        }

        public async Task<TradeResults> Start()
        {
            try
            {
                if (!_strategies.Any())
                    throw new Exception("No strategies set for this trader");

                var accCode = await _broker.ConnectAsync();
                if (accCode != "DU5962304" && accCode != "FAKEACCOUNT123")
                    throw new Exception($"Only paper trading and tests are allowed for now");

                _broker.AccountValueUpdated += OnAccountValueUpdated;
                _broker.PositionUpdated += OnPositionUpdated;
                _broker.PnLUpdated += OnPnLUpdated;
                _broker.OrderManager.OrderExecuted += OrderExecuted;

                _account = await _broker.GetAccountAsync();
                Debug.Assert(_account != null);

                if (!_account.CashBalances.ContainsKey("USD"))
                    throw new Exception($"No USD cash funds in account {_account.Code}. This trader only trades in USD.");

                IOrderedEnumerable<IStrategy> orderedStrats = _strategies.OrderBy(s => s.StartTime);
                _results.Start = orderedStrats.First().StartTime;
                _results.End = orderedStrats.Last().EndTime;
            
                _logger?.Info($"Trader started. Starting cash balance : {_account.CashBalances["USD"]:c} USD in account {_account.Code}");
                foreach (IStrategy strat in orderedStrats)
                {
                    await strat.Start();
                }

                return _results;
            }
            finally
            {
                await _broker.OrderManager.CancelAllOrdersAsync();
                await _broker.OrderManager.SellAllPositionsAsync();
            }
        }

        void OrderExecuted(string ticker, OrderExecution oe)
        {
            _logger?.Info($"{oe}");
            _results.Trades.Add(new Trade(
                Action: oe.Action,
                Qty: oe.Shares,
                Ticker: ticker, 
                Price: oe.AvgPrice,
                Commission: oe.CommissionInfo!.Commission,
                Time: oe.Time
                ));
        }

        void OnAccountValueUpdated(AccountValue val)
        {
            switch (val.Key) 
            {
                case AccountValueKey.Time:
                    _logger?.Info($"Server time : {DateTime.Parse(val.Value)}");
                    break;
                case AccountValueKey.CashBalance:
                    _logger?.Info($"Cash balance : {double.Parse(val.Value):c} {val.Currency}");
                    break;
                //case AccountValueKey.UnrealizedPnL:
                //    _logger?.Info($"UnrealizedPnL : {val.Value:c} {val.Currency}");
                //    break;
                case AccountValueKey.RealizedPnL:
                    _logger?.Info($"RealizedPnL : {double.Parse(val.Value):c} {val.Currency}");
                    break;
            }
        }

        void OnPositionUpdated(Position pos)
        {
            _logger?.Info($"Position : {pos}");
        }

        void OnPnLUpdated(PnL pnl)
        {
            _logger?.Info($"PnL : {pnl}");
        }

        bool AreOverlapping(IStrategy s1, IStrategy s2)
        {
            return s1.StartTime < s2.EndTime || s2.StartTime < s1.EndTime;
        }
    }
}