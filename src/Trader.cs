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
using TradingBot.Indicators;
using TradingBot.Broker.MarketData;

namespace TradingBot
{
    public class Trader
    {
        ILogger _logger;
        ILogger _csvLogger;
        TraderErrorHandler _errorHandler;
        IBBroker _broker;

        DateTime _startTime;
        DateTime _endTime;
        DateTime _currentTime;
        Task _monitoringTimeTask;
        CancellationTokenSource _monitoringTimeCancellation;

        string _ticker;
        string _accountCode;
        Account _account;
        Contract _contract;
        double _USDCashBalance;
        double _totalCommission;

        Position _contractPosition;
        PnL _PnL;

        bool _tradingStarted = false;
        HashSet<IStrategy> _strategies = new HashSet<IStrategy>();
        AutoResetEvent _evaluationEvent = new AutoResetEvent(false);
        
        Task _evaluationTask;
        CancellationTokenSource _evaluationTaskCancellation;

        public Trader(string ticker, DateTime startTime, DateTime endTime, int clientId) 
            : this(ticker, startTime, endTime, new IBBroker(clientId), $"{nameof(Trader)}-{ticker}_{startTime.ToShortDateString()}") {}

        internal Trader(string ticker, DateTime startTime, DateTime endTime, IBBroker broker, string loggerName)
        {
            Trace.Assert(!string.IsNullOrEmpty(ticker));
            Trace.Assert(broker != null);

            _ticker = ticker;
            _startTime = startTime;
            _broker = broker;

            // We remove 5 minutes to have the time to sell remaining positions, if any, at the end of the day.
            _endTime = endTime.AddMinutes(-5);

            var now = DateTime.Now.ToShortTimeString();
            _logger = LogManager.GetLogger(loggerName).WithProperty("now", now);
            _csvLogger = LogManager.GetLogger($"{loggerName}_Report").WithProperty("now", now);

            _errorHandler = new TraderErrorHandler(this);
            _broker.ErrorHandler = _errorHandler;
        }

        internal ILogger Logger => _logger;
        internal IBBroker Broker => _broker;
        internal Contract Contract => _contract;
        internal HashSet<IStrategy> Strategies => _strategies;
        internal bool TradingStarted => _tradingStarted;

        public void AddStrategyForTicker<TStrategy>() where TStrategy : IStrategy
        {
            _strategies.Add((IStrategy)Activator.CreateInstance(typeof(TStrategy), this));
        }

        public async Task Start()
        {
            if (!_strategies.Any())
                throw new Exception("No strategies set for this trader");
            
            var res = await _broker.Connect();
            _accountCode = res.AccountCode;

            _account = await _broker.GetAccountAsync(_accountCode);

#if DEBUG
            if (_account.Code != "DU5962304" && _account.Code != "FAKEACCOUNT123")
                throw new Exception($"In debug mode only the paper trading acount \"DU5962304\" is allowed");
#endif

            if (!_account.CashBalances.ContainsKey("USD"))
                throw new Exception($"No USD cash funds in account {_account.Code}. This trader only trades in USD.");
            _USDCashBalance = _account.CashBalances["USD"];

            _contract = await _broker.GetContract(_ticker);
            if (_contract == null)
                throw new Exception($"Unable to find contract for ticker {_ticker}.");

            _logger.Info($"Starting USD cash balance : {_USDCashBalance:c}");

            string msg = $"This trader will monitor {_ticker} using strategies : ";
            foreach (var strat in _strategies)
                msg += $"{strat.GetType().Name}, ";
            _logger.Info(msg);

            SubscribeToData();

            _currentTime = _broker.GetCurrentTime();

            _logger.Info($"Current server time : {_currentTime}");
            _logger.Info($"This trader will start trading at {_startTime} and end at {_endTime}");

            var et = StartEvaluationTask();
            var mt = StartMonitoringTimeTask();
            await Task.WhenAll(et, mt);
        }

        Task StartMonitoringTimeTask()
        {
            _monitoringTimeCancellation = new CancellationTokenSource();
            var mainToken = _monitoringTimeCancellation.Token;
            _logger.Debug($"Started monitoring current time");
            _monitoringTimeTask = Task.Factory.StartNew(() =>
            {
                try
                {
                    while (!mainToken.IsCancellationRequested && _currentTime < _endTime)
                    {
                        Task.Delay(1000).Wait();
                        _currentTime = _broker.GetCurrentTime();
                        if(!_tradingStarted && _currentTime >= _startTime)
                        {
                            _logger.Info($"Trading started!");
                            _tradingStarted = true;
                        }
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
                finally
                {
                    _broker.CancelAllOrders();
                    if (_contractPosition?.PositionAmount > 0)
                    {
                        _logger.Info($"Selling all remaining positions.");
                        SellAllPositions();
                    }
                }

                _logger.Info($"Trading ended!");

            }, mainToken);

            return _monitoringTimeTask;
        }

        void StopMonitoringTimeTask()
        {
            _monitoringTimeCancellation?.Cancel();
            _monitoringTimeCancellation?.Dispose();
            _monitoringTimeCancellation = null;
        }

        Task StartEvaluationTask()
        {
            _evaluationTaskCancellation = new CancellationTokenSource();
            var mainToken = _evaluationTaskCancellation.Token;
            _logger.Debug($"Started evaluation task.");
            _evaluationTask = Task.Factory.StartNew(() =>
            {
                try
                {
                    while (!mainToken.IsCancellationRequested)
                    {
                        _evaluationEvent.WaitOne();
                        mainToken.ThrowIfCancellationRequested();
                        foreach (IStrategy strategy in _strategies)
                            strategy.Evaluate();
                    }
                }
                catch (OperationCanceledException)
                {
                    //TODO : verify error handling
                    _logger.Trace($"Evaluation task cancelled");
                }

            }, mainToken);

            return _evaluationTask;
        }

        void StopEvaluationTask()
        {
            _evaluationTaskCancellation?.Cancel();
            _evaluationEvent?.Set();

            _evaluationEvent?.Close();
            _evaluationEvent?.Dispose();
            _evaluationTaskCancellation?.Dispose();
            _evaluationTaskCancellation = null;
        }

        void SubscribeToData()
        {
            _broker.AccountValueUpdated += OnAccountValueUpdated;
            _broker.PositionReceived += OnPositionReceived;
            _broker.PnLReceived += OnPnLReceived;

            foreach (IStrategy strategy in _strategies)
            {
                foreach(BarLength barLength in strategy.Indicators.Select(i => i.BarLength).Distinct())
                {
                    Broker.SubscribeToBarUpdateEvent(barLength, OnBarReceived);
                    Broker.RequestBarsUpdates(Contract, barLength);
                }
            }

            _broker.OrderUpdated += OnOrderUpdated;
            _broker.OrderExecuted += OnOrderExecuted;

            _broker.RequestAccountUpdates(_account.Code);
            _broker.RequestPositionsUpdates();
            _broker.RequestPnLUpdates(_contract);
        }

        void UnsubscribeToData()
        {
            _broker.AccountValueUpdated -= OnAccountValueUpdated;
            _broker.PositionReceived -= OnPositionReceived;
            _broker.PnLReceived -= OnPnLReceived;

            _broker.OrderUpdated -= OnOrderUpdated;
            _broker.OrderExecuted -= OnOrderExecuted;

            foreach (IStrategy strategy in _strategies)
            {
                foreach (BarLength barLength in strategy.Indicators.Select(i => i.BarLength).Distinct())
                {
                    Broker.UnsubscribeToBarUpdateEvent(barLength, OnBarReceived);
                    Broker.CancelBarsUpdates(Contract, barLength);
                }
            }

            _broker.CancelPositionsUpdates();
            _broker.CancelPnLUpdates(_contract);
            _broker.CancelAccountUpdates(_account.Code);
        }

        public void InitIndicators(IEnumerable<IIndicator> indicators)
        {
            Broker.InitIndicators(Contract, indicators);
        }

        void OnAccountValueUpdated(string key, string value, string currency)
        {
            switch (key)
            {
                case "CashBalance":
                    if(currency == "USD")
                    {
                        var newVal = double.Parse(value, CultureInfo.InvariantCulture);
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
                        string unrealized = position.UnrealizedPNL != double.MaxValue ? position.UnrealizedPNL.ToString("c") : "--";
                        _logger.Info($"Current Position : {position.PositionAmount} {position.Contract.Symbol} at {position.AverageCost:c}/shares (unrealized PnL : {unrealized})");
                    }
                }
                _contractPosition = position;
            }
        }

        void OnPnLReceived(PnL pnl)
        {
            if(pnl.Contract.Symbol == _ticker)
            {
                if(_PnL?.DailyPnL != pnl.DailyPnL)
                {
                    string daily = pnl.DailyPnL != double.MaxValue ? pnl.DailyPnL.ToString("c") : "--";
                    string realized = pnl.RealizedPnL != double.MaxValue ? pnl.RealizedPnL.ToString("c") : "--";
                    string unrealized = pnl.UnrealizedPnL != double.MaxValue ? pnl.UnrealizedPnL.ToString("c") : "--";
                    _logger.Info($"Daily PnL : {daily} realized : {realized}, unrealized : {unrealized}");
                }
                _PnL = pnl;
            }
        }

        void OnBarReceived(Contract contract, Bar bar)
        {
            UpdateStrategies(bar);
            EvaluateStrategies();
        }

        void OnOrderUpdated(Order o, OrderStatus os)
        {
            EvaluateStrategies();
            LogStatusChange(o, os);
        }

        void OnOrderExecuted(OrderExecution oe, CommissionInfo ci)
        {
            EvaluateStrategies();
            _totalCommission += ci.Commission;

            _logger.Info($"OrderExecuted : {_contract} {oe.Action} {oe.Shares} at {oe.AvgPrice:c} (commission : {ci.Commission:c})");
            Report(oe.Time.ToString(), _contract.Symbol, oe.Action, oe.Shares, oe.AvgPrice, oe.Shares * oe.AvgPrice, ci.Commission, ci.RealizedPNL);
        }

        private void UpdateStrategies(Bar bar)
        {
            foreach (IStrategy strategy in _strategies)
                strategy.Update(bar);
        }

        void EvaluateStrategies()
        {
            if (!_tradingStarted)
                return;

            _evaluationEvent.Set();
        }

        private void LogStatusChange(Order o, OrderStatus os)
        {
            if (os.Status == Status.Submitted || os.Status == Status.PreSubmitted)
            {
                _logger.Info($"OrderOpened : {_contract} {o} status={os.Status}");
            }
            else if (os.Status == Status.Cancelled || os.Status == Status.ApiCancelled)
            {
                _logger.Info($"OrderCancelled : {_contract} {o} status={os.Status}");
            }
            else if (os.Status == Status.Filled)
            {
                if (os.Remaining > 0)
                {
                    _logger.Info($"Order Partially filled : {_contract} {o} filled={os.Filled} remaining={os.Remaining}");
                }
                else
                {
                    _logger.Info($"Order filled : {_contract} {o}");
                }
            }
        }

        void Report(string time, string ticker, OrderAction action, double qty, double avgPrice, double totalPrice, double commission, double realizedPnL)
        {
            _csvLogger.Info("{time} {action} {qty} {price} {total} {commission} {realized}"
                , time
                , action
                , qty
                , avgPrice
                , action == OrderAction.BUY ? -totalPrice : totalPrice
                , commission
                , realizedPnL != double.MaxValue ? realizedPnL : 0.0
                );
        }

        public double GetAvailableFunds()
        {
            return _USDCashBalance - 100;
        }

        public void Stop()
        {
            // TODO : error handling? try/catch all? Separate process that monitors the main one?

            StopMonitoringTimeTask();
            StopEvaluationTask();

            _logger.Info($"Ending USD cash balance : {_USDCashBalance:c}");
            _logger.Info($"PnL for the day : {_PnL.DailyPnL:c}");

            UnsubscribeToData();
            _broker.Disconnect();
        }

        private void SellAllPositions()
        {
            MarketOrder mo = new MarketOrder()
            {
                Action = OrderAction.SELL,
                TotalQuantity = _contractPosition.PositionAmount,
            };

            // TODO : really need to implement async/await...
            var tcs = new TaskCompletionSource<bool>();
            var orderExecuted = new Action<OrderExecution, CommissionInfo>((oe, ci) =>
            {
                if (oe.OrderId == mo.Id)
                    tcs.SetResult(true);
            });
            _broker.OrderExecuted += orderExecuted;
            tcs.Task.ContinueWith(t =>
            {
                _broker.OrderExecuted -= orderExecuted;
                if (!t.IsCompletedSuccessfully)
                    throw new Exception("The trader ended with unclosed positions!");
            });

            _broker.PlaceOrder(_contract, mo);
            tcs.Task.Wait(2000);
        }
    }
}
