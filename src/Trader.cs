using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TradingBot.Broker;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.Orders;
using TradingBot.Strategies;
using System.Globalization;
using NLog;

namespace TradingBot
{
    public class Trader
    {
        ILogger _logger;
        ILogger _csvLogger;
        TraderErrorHandler _errorHandler;
        IBroker _broker;

        string _ticker;
        Contract _contract;
        double _USDCashBalance;

        // TODO track account changes
        //double _commissions;

        Position _contractPosition;
        PnL _PnL;

        HashSet<IStrategy> _strategies = new HashSet<IStrategy>();

        public Trader(string ticker)
        {
            Trace.Assert(!string.IsNullOrEmpty(ticker));

            _ticker = ticker;
            
            _logger = LogManager.GetLogger($"{nameof(Trader)}-{ticker}");
            _csvLogger = LogManager.GetLogger($"Report-{ticker}");

            _broker = new IBBroker(1337);
            
            _errorHandler = new TraderErrorHandler(this, _broker as IBBroker, _logger);
            _broker.ErrorHandler = _errorHandler;
        }

        internal Trader(string ticker, IBroker broker)
        {
            Trace.Assert(!string.IsNullOrEmpty(ticker));
            Trace.Assert(broker != null);

            _ticker = ticker;
            _logger = LogManager.GetLogger($"{nameof(Trader)}-{ticker}");
            _broker = broker;

            _errorHandler = new TraderErrorHandler(this, _broker as IBBroker, _logger);
            _broker.ErrorHandler = _errorHandler;
        }

        internal ILogger Logger => _logger;
        internal IBroker Broker => _broker;
        internal Contract Contract => _contract;
        internal HashSet<IStrategy> Strategies => _strategies;

        public void AddStrategyForTicker<TStrategy>() where TStrategy : IStrategy
        {
            _strategies.Add((IStrategy)Activator.CreateInstance(typeof(TStrategy), this));
        }

        public void Start()
        {
            _broker.Connect();
            
            if (!_strategies.Any())
                throw new Exception("No strategies set for this trader");

            var acc = _broker.GetAccount();
            if(!acc.CashBalances.ContainsKey("USD"))
                throw new Exception($"No USD cash funds in account {acc.Code}. This trader only trades in USD.");
            _USDCashBalance = acc.CashBalances["USD"];

            _contract = _broker.GetContract(_ticker);
            if (_contract == null)
                throw new Exception($"Unable to find contract for ticker {_ticker}.");

            string msg = $"This trader will monitor {_ticker} using strategies : ";
            foreach (var strat in _strategies)
                msg += $"{strat.GetType()}, ";
            _logger.Info(msg);

            SubscribeToData();

            foreach (var strat in _strategies)
                strat.Start();
        }

        void SubscribeToData()
        {
            _broker.AccountValueUpdated += OnAccountValueUpdated;
            _broker.PositionReceived += OnPositionReceived;
            _broker.PnLReceived += OnPnLReceived;

            _broker.OrderUpdated += OnOrderUpdated;
            _broker.OrderExecuted += OnOrderExecuted;

            _broker.RequestPositions();
            _broker.RequestPnL(_contract);
        }

        void UnsubscribeToData()
        {
            _broker.AccountValueUpdated -= OnAccountValueUpdated;
            _broker.PositionReceived -= OnPositionReceived;
            _broker.PnLReceived -= OnPnLReceived;

            _broker.OrderUpdated -= OnOrderUpdated;
            _broker.OrderExecuted -= OnOrderExecuted;

            _broker.CancelPositionsSubscription();
            _broker.CancelPnLSubscription(_contract);
        }

        void OnAccountValueUpdated(string key, string value, string currency)
        {
            switch (key)
            {
                case "CashBalance":
                    if(currency == "USD")
                        _USDCashBalance = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
            }
        }

        void OnPositionReceived(Position position)
        {
            if (position.Contract.Symbol == _ticker)
            {
                if(_contractPosition.PositionAmount != position.PositionAmount)
                {
                    _logger.Info($"Current Position : {position.PositionAmount} {position.Contract.Symbol} at {position.AverageCost}");
                }
                _contractPosition = position;
            }
        }

        void OnPnLReceived(PnL pnl)
        {
            if(pnl.Contract.Symbol == _ticker)
            {
                _PnL = pnl;
            }
        }

        void OnOrderUpdated(Order o, OrderStatus os)
        {
            if(os.Status == Status.Submitted || os.Status == Status.PreSubmitted)
            {
                _logger.Info($"OrderOpened : {_contract} {o} status={os.Status}");
            }
            else if(os.Status == Status.Cancelled || os.Status == Status.ApiCancelled)
            {
                _logger.Info($"OrderCancelled : {_contract} {o} status={os.Status}");
            }
            else if (os.Status == Status.Filled)
            {
                if(os.Remaining > 0)
                {
                    _logger.Info($"Order Partially filled : {_contract} {o} filled={os.Filled} remaining={os.Remaining}");
                }
                else
                {
                    _logger.Info($"Order filled : {_contract} {o}");
                }
            }
        }

        void OnOrderExecuted(OrderExecution oe, CommissionInfo ci)
        {
            _logger.Info($"OrderExecuted : {_contract} {oe.Action} {oe.Shares} at {oe.AvgPrice:c} (commission : {ci.Commission:c})");
            _csvLogger.Info("{cashBalance} {ticker} {action} {qty} {price} {total} {commission}"
                , _USDCashBalance
                , _contract.Symbol
                , oe.Action
                , oe.Shares
                , oe.AvgPrice
                , oe.Action == OrderAction.BUY ? -oe.Price : oe.Price
                , -ci.Commission);
        }

        public double GetAvailableFunds()
        {
            return _USDCashBalance;
        }

        public void Stop()
        {
            // TODO : error handling? try/catch all? Separate process that monitors the main one?

            // Kill task
            //_broker.SellEverything();

            UnsubscribeToData();
            _broker.Disconnect();
        }
    }
}
