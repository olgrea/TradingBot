using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Broker;
using Broker.Accounts;
using Broker.IBKR;
using Broker.IBKR.Client;
using Broker.Orders;
using NLog;
using Trader.Strategies;

[assembly: InternalsVisibleTo("Trader.Tests")]
namespace Trader
{
    // TODO : read record doc again
    public record struct Trade(OrderAction Action, double Qty, string Ticker, double Price, double Commission, DateTime Time);

    public class TradeResults
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public List<Trade> Trades { get; set; } = new();
        public List<(DateTime, string)> ConnectionStatuses = new();
    }

    public class Trader
    {
        const string PaperTradingAccountCodeFilePath = "./../../../../paper-trading-account.txt";

        ILogger? _logger;
        IIBBroker _broker;
        Account? _account;
        List<IStrategy> _strategies = new();
        TradeResults _results = new();
        CancellationTokenSource _cancellation = new();

        TaskCompletionSource _twsConnectionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource _marketDataConnectionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool IsConnectionLost => !_twsConnectionTcs.Task.IsCompletedSuccessfully || !_marketDataConnectionTcs.Task.IsCompletedSuccessfully;
        readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

        public Trader(IIBBroker broker, ILogger? logger = null)
        {
            _broker = broker;
            _logger = logger;
            _broker.ErrorOccured += OnErrorOccured;
            _broker.MessageReceived += OnMessageReceived;
        }

        public ILogger? Logger => _logger;
        public IIBBroker Broker => _broker;
        public Account Account => _account!;

        public void AddStrategy(IStrategy newStrat)
        {
            foreach (IStrategy strat in _strategies)
            {
                if (AreOverlapping(strat, newStrat))
                    throw new InvalidOperationException($"Multiple strategies cannot overlap during a trading day.");
            }

            _strategies.Add(newStrat);
        }

        string GetPaperTradingAccountCode()
        {
            string accCode = "NOTFOUND";
            if(File.Exists(PaperTradingAccountCodeFilePath))
            {
                accCode = File.ReadAllText(PaperTradingAccountCodeFilePath);
            }

            return accCode;
        }

        public async Task<TradeResults> Start()
        {
            try
            {
                if (!_strategies.Any())
                    throw new Exception("No strategies set for this trader");

                var accCode = await _broker.ConnectAsync();
                _twsConnectionTcs.TrySetResult();

                if (accCode != GetPaperTradingAccountCode() && accCode != "FAKEACCOUNT123")
                    throw new Exception($"Only paper trading and tests are allowed for now");

                _broker.AccountValueUpdated += OnAccountValueUpdated;
                _broker.PositionUpdated += OnPositionUpdated;
                _broker.PnLUpdated += OnPnLUpdated;
                _broker.OrderManager.OrderExecuted += OrderExecuted;

                return await StartInternal();
            }
            finally
            {
                await _broker.OrderManager.CancelAllOrdersAsync();
                await _broker.OrderManager.SellAllPositionsAsync();

                _broker.AccountValueUpdated -= OnAccountValueUpdated;
                _broker.PositionUpdated -= OnPositionUpdated;
                _broker.PnLUpdated -= OnPnLUpdated;
                _broker.OrderManager.OrderExecuted -= OrderExecuted;
                await _broker.DisconnectAsync();
            }
        }

        private async Task<TradeResults> StartInternal()
        {
            while (true)
            {
                try
                {
                    // On connection lost, we stop the currently running strategy, cancel all open orders, sell all current positions and start over.
                    if (IsConnectionLost)
                    {
                        try
                        {
                            await Task.WhenAll(_twsConnectionTcs.Task, _marketDataConnectionTcs.Task).WaitAsync(Timeout);
                        }
                        catch (TimeoutException e)
                        {
                            string message = "Market data or TWS connection lost and wasn't reestablished! You may still have open positions or have pending orders!!";
                            _logger?.Fatal(message);
                            throw new ApplicationException(message, e);
                        }

                        await Task.Delay(1000);
                        await _broker.OrderManager.CancelAllOrdersAsync(_cancellation.Token);
                        await _broker.OrderManager.SellAllPositionsAsync(_cancellation.Token);
                    }

                    _account = await _broker.GetAccountAsync(_cancellation.Token);
                    Debug.Assert(_account != null);

                    if (!_account.CashBalances.ContainsKey("USD"))
                        throw new Exception($"No USD cash funds in account {_account.Code}. This trader only trades in USD.");

                    IOrderedEnumerable<IStrategy> orderedStrats = _strategies.OrderBy(s => s.StartTime);

                    _logger?.Info($"Trader started. Starting cash balance : {_account.CashBalances["USD"]:c} USD in account {_account.Code}");

                    var currentTime = await _broker.GetServerTimeAsync(_cancellation.Token);
                    _results.Start = orderedStrats.First().StartTime;
                    if (_results.Start < currentTime)
                        _results.Start = currentTime;

                    _results.End = orderedStrats.Last().EndTime;
                    foreach (IStrategy strat in orderedStrats.SkipWhile(s => s.EndTime <= currentTime))
                    {
                        await strat.Start(_cancellation.Token);
                    }
                    return _results;
                }
                catch (AggregateException ae)
                {
                    ae.Flatten().Handle(IsConnectionLostException);
                }
                catch (Exception e) when (IsConnectionLostException(e)) { }
            }
        }

        public void Stop()
        {
            _cancellation?.Cancel();
        }

        bool IsConnectionLostException(Exception e)
        {
            return IsConnectionLost && (
                e is TaskCanceledException
                || e is OperationCanceledException
                || e is ErrorMessageException msg && Message.IsConnectionLostCode(msg.ErrorMessage.Code)
            );
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
                    _logger?.Info($"Cash balance : {double.Parse(val.Value, NumberFormatInfo.InvariantInfo):c} {val.Currency}");
                    break;
                //case AccountValueKey.UnrealizedPnL:
                //    _logger?.Info($"UnrealizedPnL : {val.Value:c} {val.Currency}");
                //    break;
                case AccountValueKey.RealizedPnL:
                    _logger?.Info($"RealizedPnL : {double.Parse(val.Value, NumberFormatInfo.InvariantInfo):c} {val.Currency}");
                    break;
            }
        }

        void OnPositionUpdated(Position pos)
        {
            _logger?.Info($"Position : {pos}");
        }

        private void OnMessageReceived(Message msg)
        {
            if (Message.IsConnectionReestablishedCode(msg.Code))
            {
                _results.ConnectionStatuses.Add((DateTime.Now, msg.Code.ToString()));

                if (msg.Code == MessageCode.MarketDataConnectionEstablished && !_marketDataConnectionTcs.Task.IsCompletedSuccessfully)
                {
                    if (_cancellation.IsCancellationRequested)
                    {
                        _cancellation.Dispose();
                        _cancellation = new();
                    }

                    _marketDataConnectionTcs.TrySetResult();
                }
                else if ((msg.Code == MessageCode.TWSConnectionRestored_MarketDataRequestsLost || msg.Code == MessageCode.TWSConnectionRestored_MarketDataRequestsRestored)
                    && !_twsConnectionTcs.Task.IsCompletedSuccessfully)
                {
                    if (_cancellation.IsCancellationRequested)
                    {
                        _cancellation.Dispose();
                        _cancellation = new();
                    }

                    _twsConnectionTcs.TrySetResult();
                }
            }
        }

        void OnErrorOccured(Exception e)
        {
            if (e is ErrorMessageException msg && Message.IsConnectionLostCode(msg.ErrorMessage.Code))
            {
                _results.ConnectionStatuses.Add((DateTime.Now, msg.ErrorMessage.Code.ToString()));

                if (msg.ErrorMessage.Code == MessageCode.TWSConnectionLost && _twsConnectionTcs.Task.IsCompletedSuccessfully)
                {
                    _twsConnectionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                else if (msg.ErrorMessage.Code == MessageCode.MarketDataConnectionLost && _marketDataConnectionTcs.Task.IsCompletedSuccessfully)
                {
                    _marketDataConnectionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            _cancellation.Cancel();
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