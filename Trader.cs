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

namespace TradingBot
{
    public class Trader
    {
        ILogger _logger;
        IBroker _broker;

        string _ticker;
        Contract _contract;
        Indicators.Indicators _indicators;

        Position _contractPosition;
        double _USDCashBalance;
                
        HashSet<IStrategy> _strategies = new HashSet<IStrategy>();
        HashSet<Type> _desiredStrategies = new HashSet<Type>();

        public Trader(string ticker, int clientId, ILogger logger)
        {
            Trace.Assert(!string.IsNullOrEmpty(ticker));

            _ticker = ticker;
            _logger = logger;
            _broker = new IBBroker(clientId, logger);
            _indicators = new Indicators.Indicators(this);
        }

        public IBroker Broker => _broker;
        public Contract Contract => _contract;
        public Indicators.Indicators Indicators => _indicators;

        public Bar Bar5Sec { get; private set; }
        public Bar Bar1Min { get; private set; }

        public void AddStrategyForTicker<TStrategy>() where TStrategy : IStrategy
        {
            _desiredStrategies.Add(typeof(TStrategy));
        }

        public void Start()
        {
            _broker.Connect();
            
            if (!_desiredStrategies.Any())
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

            _indicators.Start();

            foreach (var type in _desiredStrategies)
                _strategies.Add((IStrategy)Activator.CreateInstance(type, this));

            foreach (var strat in _strategies)
                strat.Start();
        }

        void SubscribeToData()
        {
            _broker.PositionReceived += OnPositionReceived;
            _broker.PnLReceived += OnPnLReceived;

            //_broker.Bar5SecReceived += OnBarsReceived;
            //_broker.Bar1MinReceived += OnBarsReceived;
            //_broker.BidAskReceived += OnBidAskReceived;
            
            _broker.OrderOpened += OnOrderOpened;
            _broker.OrderStatusChanged += OnOrderStatusChanged;
            _broker.OrderExecuted += OnOrderExecuted;
            _broker.CommissionInfoReceived += OnCommissionInfoReceived;

            _broker.ClientMessageReceived += OnClientMessageReceived;

            _broker.RequestPositions();
            _broker.RequestPnL(_contract);
            _broker.RequestBars(_contract, BarLength._5Sec);
            _broker.RequestBidAsk(_contract);
        }

        void UnsubscribeToData()
        {
            _broker.PositionReceived -= OnPositionReceived;
            _broker.PnLReceived -= OnPnLReceived;
            //_broker.Bar5SecReceived -= OnBarsReceived;
            //_broker.Bar1MinReceived -= OnBarsReceived;
            //_broker.BidAskReceived -= OnBidAskReceived;

            _broker.OrderOpened -= OnOrderOpened;
            _broker.OrderStatusChanged -= OnOrderStatusChanged;
            _broker.OrderExecuted -= OnOrderExecuted;
            _broker.CommissionInfoReceived -= OnCommissionInfoReceived;

            _broker.ClientMessageReceived -= OnClientMessageReceived;

            _broker.CancelPositionsSubscription();
            _broker.CancelPnLSubscription(_contract);
            _broker.CancelBidAskRequest(_contract);
            _broker.CancelBarsRequest(_contract, BarLength._5Sec);
        }

        void OnClientMessageReceived(ClientMessage message)
        {
            _logger.LogInfo($"OnClientMessageReceived : {message.Message}");
        }

        void OnPositionReceived(Position position)
        {
            if(position.Contract.Symbol == _ticker)
            {
                _contractPosition = position;
            }

            _logger.LogInfo($"OnPositionReceived : {position}");
        }

        void OnPnLReceived(PnL pnl)
        {
            _logger.LogInfo($"OnPnLReceived : {pnl}");
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

        public void Stop()
        {
            // TODO : error handling? try/catch all? Separate process that monitors the main one?

            // Kill task
            //_broker.SellEverything();
            
            _indicators.Stop();
            UnsubscribeToData();
            _broker.Disconnect();
        }
    }
}
