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
using MarketDataUtils = InteractiveBrokers.MarketData.Utils;

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
        double _totalCommission;

        Position _position;
        PnL _PnL;

        bool _tradingStarted = false;

        // TODO : only allow a single strategy? 
        // TODO : investigate how to handle multiple strategies.
        // Do a weighted mean of trade signals?
        // Only allow multiple strategies if their start-end time dont overlap?
        HashSet<IStrategy> _strategies = new HashSet<IStrategy>();
        HashSet<IIndicator> _indicatorsRequiringLastUpdates = new HashSet<IIndicator>();

        int _longestPeriod;
        Dictionary<BarLength, LinkedListWithMaxSize<Bar>> _bars = new Dictionary<BarLength, LinkedListWithMaxSize<Bar>>();

        // TODO : move start and end time in strategy?
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
        internal Contract Contract => _contract;
        Bar LatestBar
        {
            get
            {
                var smallest = _bars.Keys.Min(length => length);
                return _bars[smallest].Last();
            }
        }

        public void AddStrategyForTicker<TStrategy>() where TStrategy : IStrategy
        {
            Debug.Assert(_strategies.Count == 0, "Only one strategy is allowed for now.");
            _strategies.Add((IStrategy)Activator.CreateInstance(typeof(TStrategy), this));
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

            InitIndicators(_strategies.SelectMany(s => s.Indicators));
            SubscribeToData();
            
            _logger.Info($"Starting USD cash balance : {_account.USDCash:c}");

            string msg = $"This trader will monitor {_ticker} using strategies : ";
            foreach (var strat in _strategies)
                msg += $"{strat.GetType().Name}, ";
            _logger.Info(msg);

            _currentTime = await _client.GetCurrentTimeAsync();
            _logger.Info($"Current server time : {_currentTime}");
            _logger.Info($"This trader will start trading at {_startTime} and end at {_endTime}");

            _cancellation = new CancellationTokenSource();
            _evaluationTask = StartEvaluationTask();
            _monitoringTimeTask = StartMonitoringTimeTask();
            await Task.WhenAll(_evaluationTask, _monitoringTimeTask);
        }

        async Task StartMonitoringTimeTask()
        {
            _logger.Debug($"Started monitoring current time");
            var progress = new Progress<DateTime>(t => _currentTime = t);
            try
            {

                await _client.WaitUntil(_startTime, progress, _cancellation.Token);
                _tradingStarted = true;
                await _client.WaitUntil(_endTime, progress, _cancellation.Token);
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
            TradeSignal signal = _strategies.Select(s => s.GenerateTradeSignal(_position)).First();
            if (signal == TradeSignal.Neutral)
                return;

            //TODO : refine this. Naive implementation first.
            var latestBidAsk = await _client.GetLatestBidAskAsync(Contract);
            if (signal >= TradeSignal.Buy)
            {
                if(_position.InAny())
                {
                    // Average down?
                    //if(_PnL.UnrealizedPnL < 0)
                    //{
                    //}
                    return;
                }
                else
                {
                    double cashToInvest = 0;
                    double acceptableRisk = 0;
                    if (signal == TradeSignal.CautiousBuy)
                    {
                        cashToInvest = _account.AvailableBuyingPower / 4.0;
                        acceptableRisk = 0.05;
                    }
                    else if (signal == TradeSignal.Buy)
                    {
                        cashToInvest = _account.AvailableBuyingPower / 2.0;
                        acceptableRisk = 0.10;
                    }
                    else if (signal == TradeSignal.StrongBuy)
                    {
                        cashToInvest = _account.AvailableBuyingPower;
                        acceptableRisk = 0.15;
                    }

                    if (cashToInvest == 0)
                        return;
                    
                    int qty = (int)Math.Round(cashToInvest / latestBidAsk.Ask);
                    
                    // TODO : should use fill price;
                    double stopPrice = latestBidAsk.Ask * (1 - acceptableRisk);

                    // TODO : investigate IBKR adaptive "split spread" orders
                    var buyOrder = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = qty };
                    var stopOrder = new StopOrder() { Action = OrderAction.SELL, TotalQuantity = qty, StopPrice = stopPrice };
                    var chain = new OrderChain(buyOrder, stopOrder);
                    _orderManager.PlaceOrder(Contract, chain);
                }

            }
            else if (signal <= TradeSignal.Sell)
            {
                if (!_position.InAny())
                    return;

                int qtyToSell = 0;
                if(signal == TradeSignal.CautiousSell)
                {
                    // TODO : check other metrics
                    return;
                }
                if (signal == TradeSignal.Sell)
                {
                    qtyToSell = (int)_position.PositionAmount / 2;
                }
                else if (signal == TradeSignal.StrongSell)
                {
                    qtyToSell = (int)_position.PositionAmount;
                }

                if(qtyToSell > 0)
                {
                    var sellOrder = new MarketOrder() { Action = OrderAction.SELL, TotalQuantity = qtyToSell };
                    _orderManager.PlaceOrder(Contract, sellOrder);
                    
                    // TODO : cancel hard stop order
                    //_orderManager.CancelOrder(stopOrder);
                }
            }
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
            foreach (IStrategy strategy in _strategies)
                strategy.ComputeIndicators(quotes);
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
