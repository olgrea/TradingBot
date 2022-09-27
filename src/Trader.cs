using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using TradingBot.Broker;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Strategies;
using TradingBot.Indicators;
using TradingBot.Utils;
using System.Globalization;

namespace TradingBot
{
    public class Trader
    {
        ILogger _logger;
        TraderErrorHandler _errorHandler;
        IBroker _broker;

        string _ticker;
        Contract _contract;
        double _USDCashBalance;
        Position _contractPosition;
        PnL _PnL;

        HashSet<IStrategy> _strategies = new HashSet<IStrategy>();

        public Trader(string ticker, int clientId, ILogger logger)
        {
            Trace.Assert(!string.IsNullOrEmpty(ticker));

            _ticker = ticker;
            _logger = logger;
            _broker = new IBBroker(clientId, logger);
            
            _errorHandler = new TraderErrorHandler(this, _broker as IBBroker, _logger);
            _broker.ErrorHandler = _errorHandler;
        }

        internal Trader(string ticker, IBroker broker, ILogger logger)
        {
            Trace.Assert(!string.IsNullOrEmpty(ticker));
            Trace.Assert(broker != null);

            _ticker = ticker;
            _logger = logger;
            _broker = broker;
        }

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
            {
                _logger.LogError("No strategies set for this trader");
                return;
            }

            _contract = _broker.GetContract(_ticker);

            var acc = _broker.GetAccount();
            if(!acc.CashBalances.ContainsKey("USD"))
            {
                _logger.LogError("No USD cash funds in this account. This trader only trades in USD.");
                return;
            }
            _USDCashBalance = acc.CashBalances["USD"];

            SubscribeToData();

            foreach (var strat in _strategies)
                strat.Start();
        }

        void SubscribeToData()
        {
            _broker.AccountValueUpdated += OnAccountValueUpdated;
            _broker.PositionReceived += OnPositionReceived;
            _broker.PnLReceived += OnPnLReceived;

            _broker.OrderOpened += OnOrderOpened;
            _broker.OrderStatusChanged += OnOrderStatusChanged;
            _broker.OrderExecuted += OnOrderExecuted;
            _broker.CommissionInfoReceived += OnCommissionInfoReceived;

            _broker.RequestPositions();
            _broker.RequestPnL(_contract);
            //_broker.RequestBars(_contract, BarLength._5Sec);
            _broker.RequestBidAsk(_contract);
        }

        void UnsubscribeToData()
        {
            _broker.AccountValueUpdated -= OnAccountValueUpdated;
            _broker.PositionReceived -= OnPositionReceived;
            _broker.PnLReceived -= OnPnLReceived;

            _broker.OrderOpened -= OnOrderOpened;
            _broker.OrderStatusChanged -= OnOrderStatusChanged;
            _broker.OrderExecuted -= OnOrderExecuted;
            _broker.CommissionInfoReceived -= OnCommissionInfoReceived;

            _broker.CancelPositionsSubscription();
            _broker.CancelPnLSubscription(_contract);
            _broker.CancelBidAskRequest(_contract);
            
            //_broker.CancelBarsRequest(_contract, BarLength._5Sec);
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
                _contractPosition = position;
                _logger.LogInfo($"OnPositionReceived : {position}");
            }
        }

        void OnPnLReceived(PnL pnl)
        {
            if(pnl.Contract.Symbol == _ticker)
            {
                _PnL = pnl;
                _logger.LogInfo($"OnPnLReceived : {pnl}");
            }
        }

        void OnOrderOpened(Contract contract, Order order, OrderState state)
        {
            _logger.LogInfo($"OnOrderOpened : {contract} orderId={order.Id} status={state.Status}");
        }

        void OnOrderStatusChanged(OrderStatus status)
        {
            _logger.LogInfo($"OnOrderStatusChanged : {status.Status}");
        }

        void OnOrderExecuted(Contract contract, OrderExecution execution)
        {
            _logger.LogInfo($"OnOrderOpened : {contract} {execution}");
        }

        void OnCommissionInfoReceived(CommissionInfo commissionInfo)
        {
            _logger.LogInfo($"OnCommissionInfoReceived : {commissionInfo}");
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
