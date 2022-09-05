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
using TradingBot.Utils;

namespace TradingBot
{
    public class Trader
    {
        ILogger _logger;
        IBroker _broker;

        string _ticker;
        Contract _contract;
        
        Position _contractPosition;
        Position _USDCash;

        HashSet<IStrategy> _strategies = new HashSet<IStrategy>();
        HashSet<Type> _desiredStrategies = new HashSet<Type>();

        public Trader(string ticker, int clientId, ILogger logger)
        {
            Trace.Assert(!string.IsNullOrEmpty(ticker));

            _ticker = ticker;
            _logger = logger;
            _broker = new IBBroker(clientId, logger);
        }

        public IBroker Broker => _broker;
        public Contract Contract => _contract;

        public void AddStrategyForTicker<TStrategy>() where TStrategy : IStrategy, new()
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

            //TODO : refactor to async/await model and remove AutoResetEvent
            _contract = _broker.GetContract(_ticker);
            
            SubscribeToData();

            foreach (var type in _desiredStrategies)
                _strategies.Add((IStrategy)Activator.CreateInstance(type, _contract, this));

            foreach (var strat in _strategies)
                strat.Start();
        }

        void SubscribeToData()
        {
            _broker.PositionReceived += OnPositionReceived;
            _broker.PnLReceived += OnPnLReceived;
            _broker.Bar5SecReceived += OnBarsReceived;
            _broker.BidAskReceived += OnBidAskReceived;
            
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
            _broker.Bar5SecReceived -= OnBarsReceived;
            _broker.BidAskReceived -= OnBidAskReceived;

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
            if(position.Contract is Cash cash && cash.Currency == "USD")
            {
                _USDCash = position;
            }
            else if(position.Contract.Symbol == _ticker)
            {
                _contractPosition = position;
            }

            _logger.LogInfo($"OnPositionReceived : {position}");
        }

        void OnPnLReceived(PnL pnl)
        {
            _logger.LogInfo($"OnPnLReceived : {pnl}");
        }

        void OnBidAskReceived(Contract contract, BidAsk bidAsk)
        {
            //_logger.LogInfo($"OnBidAskReceived {DateTime.Now} : bid={bidAsk.Bid} ask={bidAsk.Ask}");
        }

        void OnBarsReceived(Contract contract, Bar bar)
        {
            //_logger.LogInfo($"OnBarsReceived {DateTime.Now} : time={bar.Time} open={bar.Open} high={bar.High} low={bar.Low} close={bar.Close}");
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

            UnsubscribeToData();
            _broker.Disconnect();
        }
    }
}
