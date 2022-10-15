using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using IBApi;
using NLog;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client.Messages;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Utils;

[assembly: InternalsVisibleTo("Tests")]
namespace TradingBot.Broker.Client
{
    //TODO : convert all of this to async/await

    internal class IBClient : IIBClient
    {
        EClientSocket _clientSocket;
        EReaderSignal _signal;
        EReader _reader;
        IBCallbacks _callbacks;
        ILogger _logger;
        Task _processMsgTask;
        string _accountCode = null;

        public IBClient(ILogger logger)
        {
            _logger = logger;
            _callbacks = new IBCallbacks(logger);
            _signal = new EReaderMonitorSignal();
            _clientSocket = new EClientSocket(_callbacks, _signal);
        }

        internal IBClient(IBCallbacks callbacks, ILogger logger)
        {
            _logger = logger;
            _callbacks = callbacks;
            _signal = new EReaderMonitorSignal();
            _clientSocket = new EClientSocket(_callbacks, _signal);
        }

        public IBCallbacks Callbacks => _callbacks;


        // TODO : really need to handle market data connection losses. It seems to happen everyday at around 8pm

        public Task<ConnectMessage> ConnectAsync(string host, int port, int clientId)
        {
            return ConnectAsync(host, port, clientId, CancellationToken.None);
        }

        public Task<ConnectMessage> ConnectAsync(string host, int port, int clientId, CancellationToken token)
        {
            //TODO: Handle IB server resets
            var msg = new ConnectMessage();
            
            var tcsConnect = new TaskCompletionSource<ConnectMessage>();
            token.ThrowIfCancellationRequested();

            var tcsValidId = new TaskCompletionSource<int>();
            var tcsAccount = new TaskCompletionSource<string>();
            var nextValidId = new Action<int>(id =>
            {
                if(token.IsCancellationRequested)
                {
                    tcsValidId.TrySetCanceled();
                    return;
                }

                _logger.Trace($"ConnectAsync : next valid id {id}");
                msg.NextValidOrderId = id;
                tcsValidId.SetResult(id);
            });

            var managedAccounts = new Action<string>(acc =>
            {
                if (token.IsCancellationRequested)
                {
                    tcsAccount.TrySetCanceled();
                    return;
                }

                _logger.Trace($"ConnectAsync : managedAccounts {acc} - set result");

                _accountCode = acc;
                msg.AccountCode = acc;
                tcsAccount.SetResult(acc);
            });

            var error = new Action<ErrorMessage>(msg => AsyncHelper<ConnectMessage>.TaskError(msg, tcsConnect, token));

            _callbacks.NextValidId += nextValidId;
            _callbacks.ManagedAccounts += managedAccounts;
            _callbacks.Error += error;

            Connect(host, port, clientId);
            
            Task.WhenAll(tcsValidId.Task, tcsAccount.Task).ContinueWith(t =>
            {
                _callbacks.NextValidId -= nextValidId;
                _callbacks.ManagedAccounts -= managedAccounts;
                _callbacks.Error -= error;

                if (token.IsCancellationRequested)
                    tcsConnect.TrySetCanceled();
                else
                    tcsConnect.SetResult(msg);
            }); ;

            return tcsConnect.Task;
        }

        void Connect(string host, int port, int clientId)
        {
            _clientSocket.eConnect(host, port, clientId);
            _reader = new EReader(_clientSocket, _signal);
            _reader.Start();
            _processMsgTask = Task.Run(ProcessMsg);
            _logger.Debug($"Reader started and is listening to messages from TWS");
        }

        void ProcessMsg()
        {
            while (_clientSocket.IsConnected())
            {
                _signal.waitForSignal();
                _reader.processMsgs();
            }
        }

        public Task<bool> DisconnectAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            _logger.Debug($"Disconnecting from TWS");

            var disconnect = new Action(() => tcs.SetResult(true));
            var error = new Action<ErrorMessage>(msg => AsyncHelper<bool>.TaskError(msg, tcs, CancellationToken.None));

            _callbacks.ConnectionClosed += disconnect;
            _callbacks.Error += error;
            tcs.Task.ContinueWith(t =>
            {
                _callbacks.ConnectionClosed -= disconnect;
                _callbacks.Error -= error;
            });

            _clientSocket.eDisconnect();

            return tcs.Task;
        }

        public Task<int> GetNextValidOrderIdAsync()
        {
            var tcs = new TaskCompletionSource<int>();

            var nextValidId = new Action<int>(id =>
            {
                tcs.SetResult(id);
            });
            var error = new Action<ErrorMessage>(msg => AsyncHelper<int>.TaskError(msg, tcs, CancellationToken.None));

            _callbacks.NextValidId += nextValidId;
            _callbacks.Error += error;
            tcs.Task.ContinueWith(t =>
            {
                _callbacks.NextValidId -= nextValidId;
                _callbacks.Error -= error;
            });

            _logger.Debug($"Requesting next valid order ids");
            _clientSocket.reqIds(-1); // param is deprecated
            return tcs.Task;
        }      

        public void RequestAccountUpdates(string accountCode)
        {
            _logger.Debug($"Requesting account updates for {accountCode}");
            _clientSocket.reqAccountUpdates(true, accountCode);
        }

        public void CancelAccountUpdates(string accountCode)
        {
            _logger.Debug($"Cancelling acount updates from account {accountCode}");
            _clientSocket.reqAccountUpdates(false, accountCode);
        }

        public void RequestPositionsUpdates()
        {
            _logger.Debug($"Requesting current positions");
            _clientSocket.reqPositions();
        }

        public void CancelPositionsUpdates()
        {
            _logger.Debug($"Cancelling positions updates");
            _clientSocket.cancelPositions();
        }

        public void RequestPnLUpdates(int reqId, int contractId)
        {
            _logger.Debug($"Requesting PnL for contract id : {contractId}");
            _clientSocket.reqPnLSingle(reqId, _accountCode, "", contractId);
        }

        public void CancelPnLUpdates(int reqId)
        {
            _logger.Debug($"Cancelling PnL subscription (reqId={reqId})");
            _clientSocket.cancelPnLSingle(reqId);
        }

        public void RequestFiveSecondsBarUpdates(int reqId, Contract contract)
        {
            _logger.Debug($"Requesting 5 sec bars for {contract} (reqId={reqId})");
            // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
            _clientSocket.reqRealTimeBars(reqId, contract.ToIBApiContract(), 5, "TRADES", true, null);
        }

        public void CancelFiveSecondsBarsUpdates(int reqId)
        {
            _logger.Debug($"Cancelling 5 sec bars for reqId={reqId}");
            _clientSocket.cancelRealTimeBars(reqId);
        }

        public void RequestTickByTickData(int reqId, Contract contract, string tickType)
        {
            _logger.Debug($"Requesting tick by tick data ({tickType}) for {contract} (reqId={reqId})");
            // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
            _clientSocket.reqTickByTickData(reqId, contract.ToIBApiContract(), tickType, 0, false);
        }

        public void CancelTickByTickData(int reqId)
        {
            _logger.Debug($"Cancelling tick by tick data for reqId={reqId}");
            _clientSocket.cancelTickByTickData(reqId);
        }

        public Task<List<ContractDetails>> GetContractDetailsAsync(int reqId, Contract contract)
        {
            var tcs = new TaskCompletionSource<List<ContractDetails>>();
            var tmpDetails = new List<ContractDetails>();
            var contractDetails = new Action<int, ContractDetails>((rId, c) =>
            {
                if (rId == reqId)
                {
                    _logger.Trace($"GetContractsAsync temp step : adding {c}");
                    tmpDetails.Add(c);
                }
            });
            var contractDetailsEnd = new Action<int>(rId =>
            {
                if (rId == reqId)
                {
                    _logger.Trace($"GetContractsAsync end step : set result");
                    tcs.SetResult(tmpDetails);
                }
            });
            var error = new Action<ErrorMessage>(msg => AsyncHelper<ErrorMessage>.TaskError(msg, tcs, CancellationToken.None));

            _callbacks.ContractDetails += contractDetails;
            _callbacks.ContractDetailsEnd += contractDetailsEnd;
            _callbacks.Error += error;

            tcs.Task.ContinueWith(t =>
            {
                _callbacks.ContractDetails -= contractDetails;
                _callbacks.ContractDetailsEnd -= contractDetailsEnd;
                _callbacks.Error -= error;
            });

            _logger.Debug($"Requesting contract details for {contract} (reqId={reqId})");
            _clientSocket.reqContractDetails(reqId, contract.ToIBApiContract());

            return tcs.Task;
        }

        public void RequestOpenOrders()
        {
            _logger.Debug($"Requesting open orders");
            _clientSocket.reqOpenOrders();
        }

        public void PlaceOrder(Contract contract, Orders.Order order)
        {
            _logger.Debug($"Requesting order placement for {contract} : {order}");
            var ibo = order.ToIBApiOrder();
            _clientSocket.placeOrder(ibo.OrderId, contract.ToIBApiContract(), ibo);
        }

        public Task<OrderMessage> PlaceOrderAsync(Contract contract, Orders.Order order)
        {
            if (order?.Id <= 0)
                throw new ArgumentException("Order id not set");

            var msg = new OrderMessage();
            var tcsMain = new TaskCompletionSource<OrderMessage>();

            var tcsOpenOrder = new TaskCompletionSource<bool>();
            var openOrder = new Action<Contract, Orders.Order, Orders.OrderState>( (c, o, oState) =>
            {
                if (order.Id == o.Id && !tcsOpenOrder.Task.IsCompleted)
                {
                    msg.Contract = c;
                    msg.Order = o;
                    msg.OrderState = oState;
                    tcsOpenOrder.TrySetResult(true);
                }
            });

            // With market orders, when the order is accepted and executes immediately, there commonly will not be any
            // corresponding orderStatus callbacks. For that reason it is recommended to also monitor the IBApi.EWrapper.execDetails .
            var tcsEnd = new TaskCompletionSource<bool>();
            var orderStatus = new Action<OrderStatus>(oStatus =>
            {
                if(order.Id == oStatus.Info.OrderId && !tcsEnd.Task.IsCompleted)
                {
                    if(oStatus.Status == Status.PreSubmitted || oStatus.Status == Status.Submitted)
                    {
                        msg.OrderStatus = oStatus;
                        tcsEnd.TrySetResult(true);
                    }
                }
            });

            var execDetails = new Action<Contract, OrderExecution>((c, oe) =>
            {
                if (order.Id == oe.OrderId && !tcsEnd.Task.IsCompleted)
                    msg.OrderExecution = oe;
            });

            var commissionReport = new Action<CommissionInfo>(ci =>
            {
                if (msg.OrderExecution.ExecId == ci.ExecId && !tcsEnd.Task.IsCompleted)
                {
                    msg.CommissionInfo = ci;
                    tcsEnd.TrySetResult(true);
                }
            });

            var error = new Action<ErrorMessage>(msg =>
            {
                if(!MarketDataUtils.IsMarketOpen() && msg.ErrorCode == 399 && msg.Message.Contains("your order will not be placed at the exchange until"))
                {
                    return;
                }

                if(tcsMain.TrySetException(msg))
                {
                    tcsOpenOrder.TrySetCanceled();
                    tcsEnd.TrySetCanceled();
                }
            });

            _callbacks.OpenOrder += openOrder;
            _callbacks.OrderStatus += orderStatus;
            _callbacks.ExecDetails += execDetails;
            _callbacks.CommissionReport += commissionReport;
            _callbacks.Error += error;

            _clientSocket.placeOrder(order.Id, contract.ToIBApiContract(), order.ToIBApiOrder());
            
            Task.WhenAll(tcsOpenOrder.Task, tcsEnd.Task).ContinueWith(t =>
            {
                _callbacks.OpenOrder -= openOrder;
                _callbacks.OrderStatus -= orderStatus;
                _callbacks.CommissionReport -= commissionReport;
                _callbacks.ExecDetails -= execDetails;
                _callbacks.Error -= error;

                if (t.IsCompletedSuccessfully)
                    tcsMain.TrySetResult(msg);
                else if (t.IsCanceled)
                    tcsMain.SetCanceled();
                else
                    tcsMain.TrySetException(new ErrorMessage("An error occured during order placement"));
            });

            return tcsMain.Task;
        }

        public void CancelOrder(int orderId)
        {
            _logger.Debug($"Requesting order cancellation for order id : {orderId}");
            _clientSocket.cancelOrder(orderId);
        }

        //TODO : to test
        public Task<OrderStatus> CancelOrderAsync(int orderId)
        {
            if (orderId <= 0)
                throw new ArgumentException("Invalid order id");

            var tcs = new TaskCompletionSource<OrderStatus>();
            var orderStatus = new Action<OrderStatus>(oStatus =>
            {
                if (orderId == oStatus.Info.OrderId)
                {
                    if (oStatus.Status == Status.ApiCancelled || oStatus.Status == Status.Cancelled)
                        tcs.TrySetResult(oStatus);
                }
            });

            var error = new Action<ErrorMessage>(msg => AsyncHelper<ConnectMessage>.TaskError(msg, tcs, CancellationToken.None));

            _callbacks.OrderStatus += orderStatus;
            _callbacks.Error += error;
            tcs.Task.ContinueWith(t =>
            {
                _callbacks.OrderStatus -= orderStatus;
                _callbacks.Error -= error;
            });

            _logger.Debug($"Requesting order cancellation for order id : {orderId}");
            _clientSocket.cancelOrder(orderId);

            return tcs.Task;
        }

        public void CancelAllOrders()
        {
            _logger.Debug($"Requesting cancellation for all open orders");
            _clientSocket.reqGlobalCancel();
        }

        public Task<LinkedList<MarketData.Bar>> GetHistoricalDataAsync(int reqId, Contract contract, BarLength barLength, DateTime endDateTime, int count)
        {
            var tmpList = new LinkedList<MarketData.Bar>();

            var resolveResult = new TaskCompletionSource<LinkedList<MarketData.Bar>>();
            SetupHistoricalBarCallbacks(tmpList, reqId, barLength, resolveResult);

            //string timeFormat = "yyyyMMdd-HH:mm:ss";

            // Duration         : Allowed Bar Sizes
            // 60 S             : 1 sec - 1 mins
            // 120 S            : 1 sec - 2 mins
            // 1800 S (30 mins) : 1 sec - 30 mins
            // 3600 S (1 hr)    : 5 secs - 1 hr
            // 14400 S (4hr)	: 10 secs - 3 hrs
            // 28800 S (8 hrs)  : 30 secs - 8 hrs
            // 1 D              : 1 min - 1 day
            // 2 D              : 2 mins - 1 day
            // 1 W              : 3 mins - 1 week
            // 1 M              : 30 mins - 1 month
            // 1 Y              : 1 day - 1 month

            string durationStr = null;
            string barSizeStr = null;
            switch (barLength)
            {
                case BarLength._1Sec:
                    durationStr = $"{count} S";
                    barSizeStr = "1 secs";
                    break;

                case BarLength._5Sec:
                    durationStr = $"{5 * count} S";
                    barSizeStr = "5 secs";
                    break;

                case BarLength._1Min:
                    durationStr = $"{60 * count} S";
                    barSizeStr = "1 min";
                    break;

                default:
                    throw new NotImplementedException($"Unable to retrieve historical data for bar lenght {barLength}");
            }

            string edt = endDateTime == DateTime.MinValue ? String.Empty : $"{endDateTime.ToString("yyyyMMdd HH:mm:ss")} US/Eastern";

            RequestHistoricalData(reqId, contract, edt, durationStr, barSizeStr, false);

            return resolveResult.Task;
        }

        private void SetupHistoricalBarCallbacks(LinkedList<MarketData.Bar> tmpList, int reqId, BarLength barLength, TaskCompletionSource<LinkedList<MarketData.Bar>> resolveResult)
        {
            var historicalData = new Action<int, MarketData.Bar>((rId, bar) =>
            {
                if (rId == reqId)
                {
                    _logger.Trace($"GetHistoricalDataAsync - historicalData - adding bar {bar.Time}");
                    bar.BarLength = barLength;
                    tmpList.AddLast(bar);
                }
            });

            var historicalDataEnd = new Action<int, string, string>((rId, start, end) =>
            {
                if (rId == reqId)
                {
                    _logger.Trace($"GetHistoricalDataAsync - historicalDataEnd - setting result");
                    resolveResult.SetResult(tmpList);
                }
            });

            _callbacks.HistoricalData += historicalData;
            _callbacks.HistoricalDataEnd += historicalDataEnd;

            resolveResult.Task.ContinueWith(t =>
            {
                _callbacks.HistoricalData -= historicalData;
                _callbacks.HistoricalDataEnd -= historicalDataEnd;
            });
        }

        public void RequestHistoricalData(int reqId, Contract contract, string endDateTime, string durationStr, string barSizeStr, bool onlyRTH)
        {
            _logger.Debug($"Requesting historical data for {contract} :\nendDateTime={endDateTime}, durationStr={durationStr}, barSizeStr={barSizeStr}, onlyRTH={onlyRTH}");
            _clientSocket.reqHistoricalData(reqId, contract.ToIBApiContract(), endDateTime, durationStr, barSizeStr, "TRADES", Convert.ToInt32(onlyRTH), 1, false, null);
        }

        public Task<IEnumerable<BidAsk>> RequestHistoricalTicks(int reqId, Contract contract, DateTime time, int count)
        {
            var tmpList = new LinkedList<BidAsk>();

            var resolveResult = new TaskCompletionSource<IEnumerable<BidAsk>>();
            var historicalTicksBidAsk = new Action<int, IEnumerable<BidAsk>, bool>((rId, bas, isDone) =>
            {
                if (rId == reqId)
                {
                    _logger.Trace($"RequestHistoricalTicks - adding {bas.Count()} bidasks");
                    
                    foreach (var ba in bas)
                        tmpList.AddLast(ba);

                    if (isDone)
                    {
                        _logger.Trace($"RequestHistoricalTicks - SetResult");
                        resolveResult.SetResult(tmpList);
                    }
                }
            });

            _callbacks.HistoricalTicksBidAsk += historicalTicksBidAsk;
            resolveResult.Task.ContinueWith(t =>
            {
                _callbacks.HistoricalTicksBidAsk -= historicalTicksBidAsk;
            });

            RequestHistoricalTicks(reqId, contract, null, $"{time.ToString("yyyyMMdd HH:mm:ss")} US/Eastern", count, "BID_ASK", false, true);

            return resolveResult.Task;
        }

        public void RequestHistoricalTicks(int reqId, Contract contract, string startDateTime, string endDateTime, int nbOfTicks, string whatToShow, bool onlyRTH, bool ignoreSize)
        {
            _logger.Debug($"Requesting historical ticks for {contract} :\nstartDateTime={startDateTime}, endDateTime={endDateTime}, nbOfTicks={nbOfTicks}, whatToShow={whatToShow}, onlyRTH={onlyRTH}");
            _clientSocket.reqHistoricalTicks(reqId, contract.ToIBApiContract(), startDateTime, endDateTime, nbOfTicks, whatToShow, Convert.ToInt32(onlyRTH), ignoreSize, null);
        }

        public Task<long> GetCurrentTimeAsync()
        {
            var tcs = new TaskCompletionSource<long>();
            
            var currentTime = new Action<long>(t => tcs.SetResult(t));
            _callbacks.CurrentTime += currentTime;
            tcs.Task.ContinueWith(task => _callbacks.CurrentTime -= currentTime);
            
            _logger.Debug("Requesting current time");
            _clientSocket.reqCurrentTime();
            return tcs.Task;
        }
    }
}
