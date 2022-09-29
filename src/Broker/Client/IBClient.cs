using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using IBApi;
using NLog;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.MarketData;

namespace TradingBot.Broker.Client
{
    internal class IBClient : IIBClient
    {
        EClientSocket _clientSocket;
        EReaderSignal _signal;
        EReader _reader;
        IBCallbacks _callbacks;
        ILogger _logger;
        Task _processMsgTask;
        int _nextValidOrderId = -1;
        string _accountCode;

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

        // TODO : really need to handle market data connection losses. It seems to happen everyday at around 8pm

        public void Connect(string host, int port, int clientId)
        {
            _clientSocket.eConnect(host, port, clientId);
            _reader = new EReader(_clientSocket, _signal);
            _reader.Start();
            _processMsgTask = Task.Run(ProcessMsg);
            _logger.Debug($"Reader started and is listening to messages from TWS");
        }

        public Task<bool> ConnectAsync(string host, int port, int clientId)
        {
            //TODO: Handle IB server resets

            var resolveResult = new TaskCompletionSource<bool>();
            var nextValidId = new Action<int>(id =>
            {
                _logger.Trace($"ConnectAsync : next valid id {id}");
                _nextValidOrderId = id;
            });

            var managedAccounts = new Action<string>(acc =>
            {
                _accountCode = acc;
                _logger.Trace($"ConnectAsync : managedAccounts {acc} - set result");
                resolveResult.SetResult(_nextValidOrderId > 0 && !string.IsNullOrEmpty(_accountCode));
            });

            _callbacks.NextValidId += nextValidId;
            _callbacks.ManagedAccounts += managedAccounts;
            resolveResult.Task.ContinueWith(t =>
            {
                _callbacks.NextValidId -= nextValidId;
                _callbacks.ManagedAccounts -= managedAccounts;

                if (_nextValidOrderId > 0)
                {
                    _logger.Info($"Client {clientId} Connected");
                }
            });

            Connect(host, port, clientId);

            return resolveResult.Task;
        }

        public IBCallbacks Callbacks => _callbacks;

        void ProcessMsg()
        {
            while (_clientSocket.IsConnected())
            {
                _signal.waitForSignal();
                _reader.processMsgs();
            }
        }

        public void Disconnect()
        {
            _logger.Debug($"Disconnecting from TWS");
            _clientSocket.eDisconnect();
        }

        public void RequestValidOrderIds()
        {
            _logger.Debug($"Requesting next valid order ids");
            _clientSocket.reqIds(-1); // param is deprecated
        }

        public Task<Account> GetAccountAsync()
        {
            var account = new Account() { Code = _accountCode };

            var resolveResult = new TaskCompletionSource<Account>();

            var updateAccountTime = new Action<string>(time =>
            {
                _logger.Trace($"GetAccountAsync updateAccountTime : {time}");
                account.Time = DateTime.Parse(time, CultureInfo.InvariantCulture);
            });
            var updateAccountValue = new Action<string, string, string>((key, value, currency) =>
            {
                _logger.Trace($"GetAccountAsync updateAccountValue : key={key}, value={value}");
                switch (key)
                {
                    case "CashBalance":
                        account.CashBalances[currency] = double.Parse(value, CultureInfo.InvariantCulture);
                        break;

                    case "RealizedPnL":
                        account.RealizedPnL[currency] = double.Parse(value, CultureInfo.InvariantCulture);
                        break;

                    case "UnrealizedPnL":
                        account.UnrealizedPnL[currency] = double.Parse(value, CultureInfo.InvariantCulture);
                        break;
                }
            });
            var updatePortfolio = new Action<Position>(pos =>
            {
                _logger.Trace($"GetAccountAsync updatePortfolio : {pos}");
                account.Positions.Add(pos);
            });
            var accountDownloadEnd = new Action<string>(accountCode =>
            {
                _logger.Trace($"GetAccountAsync accountDownloadEnd : {accountCode} - set result");
                resolveResult.SetResult(account);
            });

            _callbacks.UpdateAccountTime += updateAccountTime;
            _callbacks.UpdateAccountValue += updateAccountValue;
            _callbacks.UpdatePortfolio += updatePortfolio;
            _callbacks.AccountDownloadEnd += accountDownloadEnd;

            resolveResult.Task.ContinueWith(t =>
            {
                _callbacks.UpdateAccountTime -= updateAccountTime;
                _callbacks.UpdateAccountValue -= updateAccountValue;
                _callbacks.UpdatePortfolio -= updatePortfolio;
                _callbacks.AccountDownloadEnd -= accountDownloadEnd;

                RequestAccount(_accountCode, false);
            });

            RequestAccount(_accountCode, true);

            return resolveResult.Task;
        }

        public void RequestAccount(string accountCode, bool receiveUpdates = true)
        {
            _logger.Debug($"Requesting values from account {accountCode}");
            _clientSocket.reqAccountUpdates(receiveUpdates, accountCode);
        }

        public void RequestPositions()
        {
            _logger.Debug($"Requesting current positions");
            _clientSocket.reqPositions();
        }

        public void CancelPositions()
        {
            _logger.Debug($"Cancelling positions updates");
            _clientSocket.cancelPositions();
        }

        public void RequestPnL(int reqId, int contractId)
        {
            _logger.Debug($"Requesting PnL for contract id : {contractId}");
            _clientSocket.reqPnLSingle(reqId, _accountCode, "", contractId);
        }

        public void CancelPnL(int contractId)
        {
            _logger.Debug($"Cancelling PnL for contract id : {contractId}");
            _clientSocket.cancelPnL(contractId);
        }

        public void RequestFiveSecondsBars(int reqId, Contract contract)
        {
            _logger.Debug($"Requesting 5 sec bars for {contract} (reqId={reqId})");
            // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
            _clientSocket.reqRealTimeBars(reqId, contract.ToIBApiContract(), 5, "TRADES", true, null);
        }

        public void CancelFiveSecondsBarsRequest(int reqId)
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

        public Task<List<Contract>> GetContractsAsync(int reqId, Contract contract)
        {
            var resolveResult = new TaskCompletionSource<List<Contract>>();
            var tmpContracts = new List<Contract>();
            var contractDetails = new Action<int, Contract>((rId, c) =>
            {
                if (rId == reqId)
                {
                    _logger.Trace($"GetContractsAsync temp step : adding {c}");
                    tmpContracts.Add(c);
                }
            });
            var contractDetailsEnd = new Action<int>(rId =>
            {
                if (rId == reqId)
                {
                    _logger.Trace($"GetContractsAsync end step : set result");
                    resolveResult.SetResult(tmpContracts);
                }
            });

            _callbacks.ContractDetails += contractDetails;
            _callbacks.ContractDetailsEnd += contractDetailsEnd;

            resolveResult.Task.ContinueWith(t =>
            {
                _callbacks.ContractDetails -= contractDetails;
                _callbacks.ContractDetailsEnd -= contractDetailsEnd;
            });

            RequestContract(reqId, contract);

            return resolveResult.Task;
        }

        public void RequestContract(int reqId, Contract contract)
        {
            _logger.Debug($"Requesting contract {contract} (reqId={reqId})");
            _clientSocket.reqContractDetails(reqId, contract.ToIBApiContract());
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

        internal void PlaceOrder(Contract contract, Orders.Order order, bool whatIf)
        {
            _logger.Debug($"Requesting order placement for {contract} : {order}");
            var ibo = order.ToIBApiOrder();
            ibo.WhatIf = whatIf;
            _clientSocket.placeOrder(ibo.OrderId, contract.ToIBApiContract(), ibo);
        }

        public void CancelOrder(int orderId)
        {
            _logger.Debug($"Requesting order cancellation for order id : {orderId}");
            _clientSocket.cancelOrder(orderId);
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
    }
}
