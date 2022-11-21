using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataStorage.Db.DbCommandFactories;
using HistoricalDataFetcherApp;
using InteractiveBrokers;
using InteractiveBrokers.Accounts;
using InteractiveBrokers.Contracts;
using InteractiveBrokers.MarketData;
using InteractiveBrokers.Orders;
using NLog;
using TradingBot.Indicators;
using TradingBot.Indicators.Quotes;
using TradingBot.Strategies;
using TradingBot.Utils;

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
        TimeSpan _currentTime;
        
        CancellationTokenSource _cancellation;
        Task _monitoringTimeTask;
        Task _evaluationTask;
        AutoResetEvent _evaluationEvent = new AutoResetEvent(false);

        string _ticker;
        string _accountCode;
        Account _account;
        Contract _contract;
        double _totalCommission;

        Position _position;
        PnL _PnL;

        bool _tradingStarted = false;

        // TODO : only allow a single strategy? 
        // TODO : investigate how to handle multiple strategies.
        // Do a weighted mean of trade signals?
        // Only allow multiple strategies if their start-end time dont overlap?
        List<IStrategy> _strategies = new List<IStrategy>();
        IEnumerator<IStrategy> _strategyEnumerator;

        HashSet<IIndicator> _indicatorsRequiringLastUpdates = new HashSet<IIndicator>();

        int _longestPeriod;
        Dictionary<BarLength, LinkedListWithMaxSize<Bar>> _bars = new Dictionary<BarLength, LinkedListWithMaxSize<Bar>>();

        // TODO : move start and end time in strategy?
        public Trader(string ticker) 
            : this(ticker, new IBClient(), null) {}

        internal Trader(string ticker, IBClient client, ILogger logger)
        {
            Trace.Assert(!string.IsNullOrEmpty(ticker));
            Trace.Assert(client != null);

            _ticker = ticker;
            _client = client;

            string now = DateTime.Now.ToShortTimeString();
            _logger = logger ?? LogManager.GetLogger($"{nameof(Trader)}-{ticker}")
                .WithProperty("now", now);

            _orderManager = new OrderManager(_client, _logger);

            // We remove 5 minutes to have the time to sell remaining positions, if any, at the end of the day.
            // _endTime = endTime.AddMinutes(-5);
            
            _csvLogger = LogManager.GetLogger($"{_logger.Name}_Report").WithProperty("now", now);
        }

        internal ILogger Logger => _logger;
        internal IBClient Broker => _client;
        internal Contract Contract => _contract;
        internal IBClient Client => _client;
        internal Position Position => _position;
        internal Account Account => _account;
        internal OrderManager OrderManager => _orderManager;    

        Bar LatestBar
        {
            get
            {
                var smallest = _bars.Keys.Min(length => length);
                return _bars[smallest].Last();
            }
        }

        public void AddStrategy<TStrategy>() where TStrategy : IStrategy
        {
            IStrategy newStrat = (IStrategy)Activator.CreateInstance(typeof(TStrategy), this);

            foreach(IStrategy strat in _strategies)
            {
                if (AreOverlapping(strat, newStrat))
                    throw new InvalidOperationException($"Multiple strategies cannot overlap during a trading day.");
            }

            _strategies.Add(newStrat);
        }

        bool AreOverlapping(IStrategy s1, IStrategy s2)
        {
            return s1.StartTime >= s2.EndTime || s1.EndTime <= s2.StartTime;
        }

        public async Task Start()
        {
            if (!_strategies.Any())
                throw new Exception("No strategies set for this trader");

            _accountCode = (await _client.ConnectAsync()).AccountCode;
            _account = await _client.GetAccountAsync(_accountCode);

            if (_account.Code != "DU5962304" && _account.Code != "FAKEACCOUNT123")
                throw new Exception($"Only paper trading and tests are allowed for now");

            if (!_account.CashBalances.ContainsKey("USD"))
                throw new Exception($"No USD cash funds in account {_account.Code}. This trader only trades in USD.");

            _contract = await _client.GetContractAsync(_ticker);
            if (_contract == null)
                throw new Exception($"Unable to find contract for ticker {_ticker}.");

            _strategyEnumerator = _strategies.GetEnumerator();
            _strategyEnumerator.MoveNext();

            InitIndicators(_strategyEnumerator.Current.IndicatorStrategy.Indicators);
            SubscribeToData();
            
            _logger.Info($"Starting USD cash balance : {_account.USDCash:c}");

            string msg = $"This trader will monitor {_ticker} using strategies : ";
            foreach (var strat in _strategies)
                msg += $"{strat.GetType().Name}, ";
            _logger.Info(msg);

            _currentTime = (await _client.GetCurrentTimeAsync()).TimeOfDay;
            _logger.Info($"Current server time : {_currentTime}");

            _cancellation = new CancellationTokenSource();
            _evaluationTask = StartEvaluationTask();
            _monitoringTimeTask = StartMonitoringTimeTask();
            await Task.WhenAll(_evaluationTask, _monitoringTimeTask);
        }

        async Task StartMonitoringTimeTask()
        {
            _logger.Debug($"Started monitoring current time");
            var progress = new Progress<TimeSpan>(t => _currentTime = t);
            try
            {
                foreach(IStrategy strat in _strategies.OrderBy(s => s.StartTime))
                {
                    await _client.WaitUntil(strat.StartTime, progress, _cancellation.Token);
                    _tradingStarted = true;
                    await _client.WaitUntil(strat.EndTime, progress, _cancellation.Token);
                }
            }
            finally
            {
                _client.CancelAllOrders();
                if (_position?.PositionAmount > 0)
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

        //TODO : rework this approach
        async Task StartEvaluationTask()
        {
            var mainToken = _cancellation.Token;
            _logger.Debug($"Started evaluation task.");
            try
            {
                while (!mainToken.IsCancellationRequested)
                {
                    // TODO : correct sync data structure?
                    _evaluationEvent.WaitOne();
                    mainToken.ThrowIfCancellationRequested();
                    await EvaluateStrategies();
                }
            }
            catch (OperationCanceledException) {}

            await Task.CompletedTask;
        }

        private async Task EvaluateStrategies()
        {
            TradeSignal signal = _strategyEnumerator.Current.IndicatorStrategy.GenerateTradeSignal(_position);
            await _strategyEnumerator.Current.OrderStrategy.ManageOrders(signal);
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
            foreach(BarLength barLength in _strategyEnumerator.Current.IndicatorStrategy.Indicators.Select(i => i.BarLength).Distinct())
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
            foreach (BarLength barLength in _strategyEnumerator.Current.IndicatorStrategy.Indicators.Select(i => i.BarLength).Distinct())
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
                    _bars[barLength].Add(MarketDataUtils.CombineBars(bars.Take(nbSecs), barLength));
                    bars = bars.Skip(nbSecs);
                }
            }

            // Update all indicators.
            foreach (var indicator in indicators)
            {
                indicator.Compute(_bars[indicator.BarLength].Select(b => (BarQuote)b));
            }

            Debug.Assert(indicators.All(i => i.IsReady));
        }


        public void RequestLastTradedPricesUpdates(IIndicator indicator)
        {
            if(!_indicatorsRequiringLastUpdates.Contains(indicator))
            {
                _indicatorsRequiringLastUpdates.Add(indicator);
                if(_indicatorsRequiringLastUpdates.Count == 1)
                {
                    _client.LastReceived += OnLastReceived;
                    _client.RequestLastUpdates(_contract);
                }
            }
        }

        void OnLastReceived(Contract contract, Last last)
        {
            foreach(var indicator in _indicatorsRequiringLastUpdates)
            {
                indicator.ComputeTrend(last);
            }
            SetEvaluationEvent();
        }

        public void CancelLastTradedPricesUpdates(IIndicator indicator)
        {
            if(_indicatorsRequiringLastUpdates.Remove(indicator))
            {
                if (_indicatorsRequiringLastUpdates.Count == 0)
                {
                    _client.LastReceived -= OnLastReceived;
                    _client.CancelLastUpdates(_contract);
                }
            }
        }

        void OnAccountValueUpdated(string key, string value, string currency, string account)
        {
            switch (key)
            {
                case "CashBalance":
                    if(currency == "USD")
                    {
                        var newVal = double.Parse(value, CultureInfo.InvariantCulture);
                        if(newVal != _account.USDCash)
                        {
                            _logger.Info($"New Account Cash balance : {newVal:c} USD");
                            _account.USDCash = newVal;
                        }
                    }
                    break;
            }
        }

        void OnPositionReceived(Position position)
        {
            if (position.Contract.Symbol == _ticker)
            {
                if(_position?.PositionAmount != position.PositionAmount)
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
                _position = position;
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
                SetEvaluationEvent();
            }
        }

        void OnOrderUpdated(Order o, OrderStatus os)
        {
            SetEvaluationEvent();
            LogStatusChange(o, os);
        }

        void OnOrderExecuted(OrderExecution oe, CommissionInfo ci)
        {
            SetEvaluationEvent();
            _totalCommission += ci.Commission;

            _logger.Info($"OrderExecuted : {_contract} {oe.Action} {oe.Shares} at {oe.AvgPrice:c} (commission : {ci.Commission:c})");
            Report(oe.Time.ToString(), _contract.Symbol, oe.Action, oe.Shares, oe.AvgPrice, oe.Shares * oe.AvgPrice, ci.Commission, ci.RealizedPNL);
        }

        private void UpdateStrategies(IEnumerable<Bar> bars)
        {
            var quotes = bars.Select(b => (BarQuote)b);
            _strategyEnumerator.Current.IndicatorStrategy.ComputeIndicators(quotes);
        }

        void SetEvaluationEvent()
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

        public async void Stop()
        {
            StopMonitoringTimeTask();
            StopEvaluationTask();

            _logger.Info($"Ending USD cash balance : {_account.USDCash:c}");
            _logger.Info($"PnL for the day : {_PnL.DailyPnL:c}");

            UnsubscribeToData();
            await _client.DisconnectAsync();
        }

        private void SellAllPositions()
        {
            MarketOrder mo = new MarketOrder()
            {
                Action = OrderAction.SELL,
                TotalQuantity = _position.PositionAmount,
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
