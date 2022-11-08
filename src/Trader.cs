using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TradingBot.Strategies;
using System.Globalization;
using NLog;
using System.Threading.Tasks;
using System.Threading;
using TradingBot.Indicators;
using InteractiveBrokers;
using InteractiveBrokers.Accounts;
using InteractiveBrokers.Contracts;
using InteractiveBrokers.MarketData;
using InteractiveBrokers.Orders;
using DataStorage.Db.DbCommandFactories;
using HistoricalDataFetcherApp;
using TradingBot.Utils;
using MarketDataUtils = InteractiveBrokers.MarketData.Utils;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Backtester")]
[assembly: Fody.ConfigureAwait(false)]

namespace TradingBot
{
    public class Trader
    {
        ILogger _logger;
        ILogger _csvLogger;
        IBClient _client;
        OrderManager _orderManager;

        DateTime _startTime;
        DateTime _endTime;
        DateTime _currentTime;
        
        CancellationTokenSource _cancellation;
        Task _monitoringTimeTask;
        Task _evaluationTask;
        AutoResetEvent _evaluationEvent = new AutoResetEvent(false);

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

        int _longestPeriod;
        Dictionary<BarLength, LinkedListWithMaxSize<Bar>> _bars = new Dictionary<BarLength, LinkedListWithMaxSize<Bar>>();

        public Trader(string ticker, DateTime startTime, DateTime endTime, int clientId) 
            : this(ticker, startTime, endTime, new IBClient(clientId), $"{nameof(Trader)}-{ticker}_{startTime.ToShortDateString()}") {}

        internal Trader(string ticker, DateTime startTime, DateTime endTime, IBClient client, string loggerName)
        {
            Trace.Assert(!string.IsNullOrEmpty(ticker));
            Trace.Assert(client != null);

            _ticker = ticker;
            _startTime = startTime;
            _client = client;
            _orderManager = new OrderManager(_client, _logger);

            // We remove 5 minutes to have the time to sell remaining positions, if any, at the end of the day.
            _endTime = endTime.AddMinutes(-5);

            var now = DateTime.Now.ToShortTimeString();
            _logger = LogManager.GetLogger(loggerName).WithProperty("now", now);
            _csvLogger = LogManager.GetLogger($"{loggerName}_Report").WithProperty("now", now);
        }

        internal ILogger Logger => _logger;
        internal IBClient Broker => _client;
        internal OrderManager OrderManager => _orderManager;
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
            
            var res = await _client.ConnectAsync();
            _accountCode = res.AccountCode;
            _account = await _client.GetAccountAsync(_accountCode);

            if (_account.Code != "DU5962304" && _account.Code != "FAKEACCOUNT123")
                throw new Exception($"Only paper trading and tests are allowed for now");

            if (!_account.CashBalances.ContainsKey("USD"))
                throw new Exception($"No USD cash funds in account {_account.Code}. This trader only trades in USD.");
            _USDCashBalance = _account.CashBalances["USD"];

            _contract = await _client.GetContractAsync(_ticker);
            if (_contract == null)
                throw new Exception($"Unable to find contract for ticker {_ticker}.");

            InitIndicators(_strategies.SelectMany(s => s.Indicators));
            SubscribeToData();
            
            _logger.Info($"Starting USD cash balance : {_USDCashBalance:c}");

            string msg = $"This trader will monitor {_ticker} using strategies : ";
            foreach (var strat in _strategies)
                msg += $"{strat.GetType().Name}, ";
            _logger.Info(msg);

            _currentTime = await _client.GetCurrentTimeAsync();
            _logger.Info($"Current server time : {_currentTime}");
            _logger.Info($"This trader will start trading at {_startTime} and end at {_endTime}");

            _cancellation = new CancellationTokenSource();
            var et = StartEvaluationTask();
            _monitoringTimeTask = StartMonitoringTimeTask();
            await Task.WhenAll(et, _monitoringTimeTask);
        }

        async Task StartMonitoringTimeTask()
        {
            var mainToken = _cancellation.Token;
            _logger.Debug($"Started monitoring current time");
            try
            {
                while (!mainToken.IsCancellationRequested && _currentTime < _endTime)
                {
                    await Task.Delay(500);
                    _currentTime = await _client.GetCurrentTimeAsync();
                    if(!_tradingStarted && _currentTime >= _startTime)
                    {
                        _logger.Info($"Trading started!");
                        _tradingStarted = true;
                    }
                }
            }
            finally
            {
                _client.CancelAllOrders();
                if (_contractPosition?.PositionAmount > 0)
                {
                    _logger.Info($"Selling all remaining positions.");
                    SellAllPositions();
                }

                _logger.Info($"Trading ended!");
            }
        }

        void StopMonitoringTimeTask()
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
        }

        Task StartEvaluationTask()
        {
            var mainToken = _cancellation.Token;
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
                catch (OperationCanceledException) {}

            }, mainToken);

            return _evaluationTask;
        }

        void StopEvaluationTask()
        {
            _evaluationEvent?.Set();
            _evaluationEvent?.Close();
            _evaluationEvent?.Dispose();
        }

        void SubscribeToData()
        {
            _client.AccountValueUpdated += OnAccountValueUpdated;
            _client.PositionReceived += OnPositionReceived;
            _client.PnLReceived += OnPnLReceived;

            _client.RequestBarsUpdates(Contract);
            foreach(BarLength barLength in _strategies.SelectMany(s => s.Indicators).Select(i => i.BarLength).Distinct())
                _client.BarReceived[barLength] += OnBarReceived;

            _orderManager.OrderUpdated += OnOrderUpdated;
            _orderManager.OrderExecuted += OnOrderExecuted;

            _client.RequestAccountUpdates(_account.Code);
            _client.RequestPositionsUpdates();
            _client.RequestPnLUpdates(_contract);
        }

        void UnsubscribeToData()
        {
            _client.AccountValueUpdated -= OnAccountValueUpdated;
            _client.PositionReceived -= OnPositionReceived;
            _client.PnLReceived -= OnPnLReceived;

            _orderManager.OrderUpdated -= OnOrderUpdated;
            _orderManager.OrderExecuted -= OnOrderExecuted;

            Broker.CancelBarsUpdates(Contract);
            foreach (BarLength barLength in _strategies.SelectMany(s => s.Indicators).Select(i => i.BarLength).Distinct())
                _client.BarReceived[barLength] -= OnBarReceived;

            _client.CancelPositionsUpdates();
            _client.CancelPnLUpdates(_contract);
            _client.CancelAccountUpdates(_account.Code);
        }

        public async void InitIndicators(IEnumerable<IIndicator> indicators)
        {
            if (!indicators.Any())
                return;

            // How many past bars do we need?
            int longestNbOfOneSecBarsNeededForInit = indicators.Max(i => i.NbWarmupPeriods * (int)i.BarLength);
            _longestPeriod = longestNbOfOneSecBarsNeededForInit;

            var fetcher = new HistoricalDataFetcher(_client, null);
            DateTime currentTime = await _client.GetCurrentTimeAsync();
            IEnumerable<Bar> allBars = Enumerable.Empty<Bar>();
            var barCmdFactory = new BarCommandFactory(BarLength._1Sec);

            // Get the ones from today : from opening to now
            if (currentTime.TimeOfDay > MarketDataUtils.MarketStartTime)
            {
                allBars = await fetcher.GetDataForDay<Bar>(currentTime.Date, (MarketDataUtils.MarketStartTime, currentTime.TimeOfDay), _contract, barCmdFactory);
            }

            int count = allBars.Count();
            if (count < longestNbOfOneSecBarsNeededForInit)
            {
                // Not enough. Getting previous market day to fill the rest.
                int rest = longestNbOfOneSecBarsNeededForInit - count;

                var previousMarketDay = currentTime;
                IEnumerable<Bar> previousMarketDayBars = null;
                while (previousMarketDayBars == null)
                {
                    previousMarketDay = previousMarketDay.AddDays(-1);
                    try
                    {
                        if (!MarketDataUtils.IsWeekend(previousMarketDay))
                        {
                            var start = new TimeSpan(MarketDataUtils.MarketEndTime.Ticks - TimeSpan.FromSeconds(rest).Ticks);
                            previousMarketDayBars = await fetcher.GetDataForDay<Bar>(previousMarketDay.Date, (start, MarketDataUtils.MarketEndTime), _contract, barCmdFactory);
                            allBars = previousMarketDayBars.Concat(allBars);
                        }
                    }
                    catch (MarketHolidayException) { }
                }
            }

            // build bar collections from 1 sec bars
            foreach (var barLength in indicators.Select(i => i.BarLength).Distinct())
            {
                if (!_bars.ContainsKey(barLength))
                    _bars[barLength] = new LinkedListWithMaxSize<Bar>(_longestPeriod);

                var nbSecs = (int)barLength;

                var bars = allBars;
                while (bars.Count() > nbSecs)
                {
                    _bars[barLength].Add(MarketDataUtils.MakeBar(bars.Take(nbSecs), barLength));
                    bars = bars.Skip(nbSecs);
                }
            }

            // Update all indicators.
            foreach (var indicator in indicators)
            {
                indicator.Compute(_bars[indicator.BarLength].Cast<BarQuote>());
            }

            Debug.Assert(indicators.All(i => i.IsReady));
        }

        void OnAccountValueUpdated(string key, string value, string currency, string account)
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
            if(contract.Symbol == _ticker)
            {
                if (!_bars.ContainsKey(bar.BarLength))
                    _bars[bar.BarLength] = new LinkedListWithMaxSize<Bar>(_longestPeriod);

                _bars[bar.BarLength].Add(bar);

                UpdateStrategies(_bars[bar.BarLength]);
                EvaluateStrategies();
            }
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

        private void UpdateStrategies(IEnumerable<Bar> bars)
        {
            foreach (IStrategy strategy in _strategies)
                strategy.ComputeIndicators(bars);
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

        public async void Stop()
        {
            StopMonitoringTimeTask();
            StopEvaluationTask();

            _logger.Info($"Ending USD cash balance : {_USDCashBalance:c}");
            _logger.Info($"PnL for the day : {_PnL.DailyPnL:c}");

            UnsubscribeToData();
            await _client.DisconnectAsync();
        }

        private void SellAllPositions()
        {
            MarketOrder mo = new MarketOrder()
            {
                Action = OrderAction.SELL,
                TotalQuantity = _contractPosition.PositionAmount,
            };

            var tcs = new TaskCompletionSource<bool>();
            var orderExecuted = new Action<OrderExecution, CommissionInfo>((oe, ci) =>
            {
                if (oe.OrderId == mo.Id)
                    tcs.SetResult(true);
            });

            _orderManager.OrderExecuted += orderExecuted;
            tcs.Task.ContinueWith(t =>
            {
                _orderManager.OrderExecuted -= orderExecuted;
                if (!t.IsCompletedSuccessfully)
                    throw new Exception("The trader ended with unclosed positions!");
            });

            _client.PlaceOrder(_contract, mo);
            tcs.Task.Wait(2000);
        }
    }
}
