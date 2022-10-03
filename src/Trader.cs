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
using System.Threading.Tasks;
using System.Threading;

namespace TradingBot
{
    public class Trader
    {
        ILogger _logger;
        ILogger _csvLogger;
        TraderErrorHandler _errorHandler;
        IBroker _broker;

        DateTime _startTime;
        DateTime _endTime;
        DateTime _currentTime;
        Task _monitoringTimeTask;
        CancellationTokenSource _monitoringTimeCancellation;

        string _ticker;
        Contract _contract;
        double _USDCashBalance;

        Position _contractPosition;
        PnL _PnL;

        HashSet<IStrategy> _strategies = new HashSet<IStrategy>();
        bool _strategiesStarted = false;

        public Trader(string ticker, DateTime startTime, DateTime endTime) : this(ticker, startTime, endTime, new IBBroker())
        {

        }

        internal Trader(string ticker, DateTime startTime, DateTime endTime, IBroker broker)
        {
            Trace.Assert(!string.IsNullOrEmpty(ticker));
            Trace.Assert(broker != null);

            _ticker = ticker;
            _startTime = startTime;
            _endTime = endTime;
            _broker = broker;
            
            _logger = LogManager.GetLogger($"{nameof(Trader)}-{ticker}");
            _csvLogger = LogManager.GetLogger($"Report-{ticker}");

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

        public Task Start()
        {
            if (!_strategies.Any())
                throw new Exception("No strategies set for this trader");
            
            _broker.Connect();

            var acc = _broker.GetAccount();
            if(!acc.CashBalances.ContainsKey("USD"))
                throw new Exception($"No USD cash funds in account {acc.Code}. This trader only trades in USD.");
            _USDCashBalance = acc.CashBalances["USD"];

            _contract = _broker.GetContract(_ticker);
            if (_contract == null)
                throw new Exception($"Unable to find contract for ticker {_ticker}.");

            _logger.Info($"Starting USD cash balance : {_USDCashBalance:c}");

            string msg = $"This trader will monitor {_ticker} using strategies : ";
            foreach (var strat in _strategies)
                msg += $"{strat.GetType()}, ";
            _logger.Info(msg);

            SubscribeToData();

            _currentTime = _broker.GetCurrentTime();
            return StartMonitoringTimeTask();
        }

        Task StartMonitoringTimeTask()
        {
            _monitoringTimeCancellation = new CancellationTokenSource();
            var mainToken = _monitoringTimeCancellation.Token;
            _logger.Debug($"Started monitoring current time");
            _monitoringTimeTask = Task.Factory.StartNew(() =>
            {
                while (!mainToken.IsCancellationRequested && _currentTime < _endTime)
                {
                    try
                    {
                        Task.Delay(1000).Wait();
                        _currentTime = _broker.GetCurrentTime();
                        if(!_strategiesStarted && _currentTime >= _startTime)
                        {
                            _strategiesStarted = true;
                            foreach (var strat in _strategies)
                                strat.Start();
                        }

                    }
                    catch (AggregateException e)
                    {
                        //TODO : verify error handling
                        if (e.InnerException is OperationCanceledException)
                        {
                            _logger.Trace($"Monitoring time task cancelled");
                            return;
                        }

                        throw e;
                    }
                }

            }, mainToken);

            return _monitoringTimeTask;
        }

        void StopMonitoringTimeTask()
        {
            _monitoringTimeCancellation?.Cancel();
            _monitoringTimeCancellation?.Dispose();
            _monitoringTimeCancellation = null;
            _monitoringTimeCancellation = null;
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
                    {
                        var newVal = double.Parse(value, CultureInfo.CurrentCulture);
                        if(newVal != _USDCashBalance)
                        {
                            _logger.Info($"New Account Cash balance : {newVal:c} USD");
                            _USDCashBalance = newVal;
                        }
                    }
                    break;
            }
        }

        void OnPositionReceived(Position position)
        {
            if (position.Contract.Symbol == _ticker)
            {
                if(_contractPosition?.PositionAmount != position.PositionAmount)
                {
                    if (position.PositionAmount == 0)
                    {
                        _logger.Info($"Current Position : none");
                    }
                    else 
                    {
                        _logger.Info($"Current Position : {position.PositionAmount} {position.Contract.Symbol} at {position.AverageCost:c}/shares");
                    }
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
            Report(oe.Time.ToString(), _contract.Symbol, oe.Action, oe.Shares, oe.AvgPrice, oe.Price, ci.Commission);
        }

        void Report(string time, string ticker, OrderAction action, double qty, double avgPrice, double totalPrice, double commission)
        {
            _csvLogger.Info("{action} {qty} {price} {total} {commission} {time}"
                , action
                , qty
                , avgPrice
                , action == OrderAction.BUY ? -totalPrice : totalPrice
                , -commission
                , time
                );
        }

        public double GetAvailableFunds()
        {
            return _USDCashBalance;
        }

        public void Stop()
        {
            // TODO : error handling? try/catch all? Separate process that monitors the main one?

            StopMonitoringTimeTask();

            foreach (var strat in _strategies)
                strat.Stop();

            _broker.CancelAllOrders();
            if(_contractPosition?.PositionAmount > 0)
            {
                _broker.PlaceOrder(_contract, new MarketOrder()
                {
                    Action = OrderAction.SELL,
                    TotalQuantity = _contractPosition.PositionAmount,
                });
            }

            _logger.Info($"Ending USD cash balance : {_USDCashBalance:c}");

            UnsubscribeToData();
            _broker.Disconnect();
        }
    }
}
