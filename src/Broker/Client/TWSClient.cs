using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using IBApi;
using TradingBot.Utils;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;

using TBOrder = TradingBot.Broker.Orders.Order;
using TBOrderState = TradingBot.Broker.Orders.OrderState;
using TradingBot.Broker.Accounts;

namespace TradingBot.Broker.Client
{
    internal class TWSClient : EWrapper
    {
        internal class Connector
        {
            int _nextValidOrderId = -1;
            int _reqId = 0;

            int _clientId = -1;
            EReaderSignal _signal;
            EReader _reader;

            ILogger _logger;
            Task _processMsgTask;
            string _accountCode = null;

            public Connector(EWrapper eWrapper, ILogger logger)
            {
                _signal = new EReaderMonitorSignal();
                ClientSocket = new EClientSocket(eWrapper, _signal);
                _logger = logger;
            }

            public EClientSocket ClientSocket { get; private set; }
            public int NextRequestId => _reqId++;
            public int NextValidOrderId => _nextValidOrderId++;
            public string AccountCode => _accountCode;
            public Dictionary<Contract, int> BidAskSubscriptions { get; private set; } = new Dictionary<Contract, int>();
            public Dictionary<Contract, int> FiveSecSubscriptions { get; private set; } = new Dictionary<Contract, int>();
            public Dictionary<Contract, int> PnlSubscriptions { get; private set; } = new Dictionary<Contract, int>();


            public event Action ClientConnected;
            public event Action ClientDisconnected;
            public event Action<ClientMessage> ClientMessageReceived;
            event Action<int> _nextValidIdEvent;
            event Action<string> _managedAccountsEvent;

            public Task<bool> ConnectAsync(string host, int port, int clientId)
            {
                //TODO: Handle IB server resets

                _clientId = clientId;

                var resolveResult = new TaskCompletionSource<bool>();
                var nextValidId = new Action<int>(id =>
                {
                    if (_nextValidOrderId < 0)
                    {
                        _logger.LogInfo($"Client connected.");
                    }
                    _nextValidOrderId = id;
                });

                var managedAccounts = new Action<string>(acc =>
                {
                    _accountCode = acc;
                    resolveResult.SetResult(_nextValidOrderId > 0 && !string.IsNullOrEmpty(_accountCode));
                });

                var error = new Action<ClientMessage>(msg => TaskError(msg, resolveResult));

                _nextValidIdEvent += nextValidId;
                _managedAccountsEvent += managedAccounts;
                ClientMessageReceived += error;
                resolveResult.Task.ContinueWith(t =>
                {
                    _nextValidIdEvent -= nextValidId;
                    _managedAccountsEvent -= managedAccounts;
                    ClientMessageReceived -= error;

                    if (_nextValidOrderId > 0)
                        ClientConnected?.Invoke();
                });

                ClientSocket.eConnect(host, port, clientId);
                _reader = new EReader(ClientSocket, _signal);
                _reader.Start();
                _processMsgTask = Task.Run(ProcessMsg);
                _logger.LogDebug($"Reader started and is listening to messages from TWS");

                return resolveResult.Task;
            }

            public void Disconnect()
            {
                ClientSocket.reqAccountUpdates(false, _accountCode);
                foreach (var kvp in BidAskSubscriptions)
                    ClientSocket.cancelTickByTickData(kvp.Value);

                foreach (var kvp in FiveSecSubscriptions)
                    ClientSocket.cancelRealTimeBars(kvp.Value);

                foreach (var kvp in PnlSubscriptions)
                    ClientSocket.cancelPnL(kvp.Value);

                ClientSocket.eDisconnect();
                _clientId = -1;
                ClientDisconnected?.Invoke();
            }

            public bool IsConnected => ClientSocket.IsConnected() && _nextValidOrderId > 0;

            void ProcessMsg()
            {
                while (ClientSocket.IsConnected())
                {
                    _signal.waitForSignal();
                    _reader.processMsgs();
                }
            }

            public void connectAck()
            => _logger.LogDebug($"Connecting client to TWS...");

            public void connectionClosed()
            => _logger.LogDebug($"Connection closed");

            public void managedAccounts(string accountsList)
            {
                _logger.LogDebug($"Account list : {accountsList}");
                _managedAccountsEvent?.Invoke(accountsList);
            }

            public int GetNextValidOrderId(bool fromTWS = false)
            {
                if (fromTWS)
                {
                    return GetNextValidOrderIdAsync().Result;
                }
                return NextValidOrderId;
            }

            Task<int> GetNextValidOrderIdAsync()
            {
                var resolveResult = new TaskCompletionSource<int>();
                var nextValidId = new Action<int>(id => resolveResult.SetResult(id));
                var error = new Action<ClientMessage>(msg => TaskError(msg, resolveResult));

                _nextValidIdEvent += nextValidId;
                ClientMessageReceived += error;
                resolveResult.Task.ContinueWith(t =>
                {
                    _nextValidIdEvent -= nextValidId;
                    ClientMessageReceived -= error;
                });

                ClientSocket.reqIds(-1); // param is deprecated

                return resolveResult.Task;
            }

            // The next valid identifier is persistent between TWS sessions
            public void nextValidId(int orderId)
            {
                _nextValidIdEvent?.Invoke(orderId);
            }

            public void error(Exception e)
            {
                _logger.LogError(e.Message);
                ClientMessageReceived?.Invoke(new ClientException(e));
            }

            public void error(string str)
            {
                _logger.LogError(str);
                ClientMessageReceived?.Invoke(new ClientError(str));
            }

            public void error(int id, int errorCode, string errorMsg)
            {
                var str = $"{id} {errorCode} {errorMsg}";
                if (errorCode == 502)
                    str += $"\nMake sure the API is enabled in Trader Workstation";


                // Note: id == -1 indicates a notification and not true error condition...
                ClientMessage msg;
                if (id < 0)
                {
                    _logger.LogDebug(str);
                    msg = new ClientNotification(errorMsg);
                }
                else
                {
                    _logger.LogError(str);
                    msg = new ClientError(id, errorCode, errorMsg);
                }

                ClientMessageReceived?.Invoke(msg);
            }

            public void TaskError<T>(ClientMessage msg, TaskCompletionSource<T> resolveResult)
            {
                if (msg is ClientError)
                {
                    if (msg is ClientException ex)
                        resolveResult.SetException(ex.Exception);
                    resolveResult.SetResult(default(T));
                }
            }
        }

        Connector _connector;
        ILogger _logger;
        
        MarketData.BidAsk _bidAsk = new BidAsk();

        public event Action<PnL> PnLReceived;
        public event Action<Position> PositionReceived;
        public event Action<string, string, string> AccountValueUpdated;

        public event Action<Contract, MarketData.Bar> FiveSecBarReceived;
        public event Action<Contract, BidAsk> BidAskReceived;
        public event Action<Contract, TBOrder, TBOrderState> OrderOpened;
        public event Action<OrderStatus> OrderStatusChanged;
        public event Action<Contract, OrderExecution> OrderExecuted;
        public event Action<CommissionInfo> CommissionInfoReceived;

        event Action<DateTime> _updateAccountTimeEvent;
        event Action<Position> _updatePortfolioEvent;
        event Action<string> _accountDownloadEndEvent;
        event Action<int, Contract> _contractDetailsEvent;
        event Action<int> _contractDetailsEndEvent;
        event Action<int, MarketData.Bar> _historicalDataEvent;
        event Action<int, string, string> _historicalDataEndEvent;

        public event Action ClientConnected
        {
            add => _connector.ClientConnected += value;
            remove => _connector.ClientConnected -= value;
        }

        public event Action ClientDisconnected
        {
            add => _connector.ClientDisconnected += value;
            remove => _connector.ClientDisconnected -= value;
        }

        public event Action<ClientMessage> ClientMessageReceived
        {
            add => _connector.ClientMessageReceived += value;
            remove => _connector.ClientMessageReceived -= value;
        }

        // TODO : move this to a data subscription manager?
        Dictionary<Contract, int> BidAskSubscriptions => _connector.BidAskSubscriptions;
        Dictionary<Contract, int> FiveSecSubscriptions => _connector.FiveSecSubscriptions;
        Dictionary<Contract, int> PnlSubscriptions => _connector.PnlSubscriptions;

        public TWSClient(ILogger logger)
        {
            _connector = new Connector(this, logger);
            _logger = logger;
        }

        public Task<bool> ConnectAsync(string host, int port, int clientId) => _connector.ConnectAsync(host, port, clientId);
        public void Disconnect() => _connector.Disconnect();
        public bool IsConnected => _connector.IsConnected;
        public void connectAck() => _connector.connectAck();
        public void connectionClosed() => _connector.connectionClosed();
        public void managedAccounts(string accountsList) => _connector.managedAccounts(accountsList);
        public void nextValidId(int orderId) => _connector.nextValidId(orderId);
        public int GetNextValidOrderId(bool fromTWS = false) => _connector.GetNextValidOrderId(fromTWS);
        
        public Task<Account> GetAccountAsync(bool receiveUpdates = true)
        {
            var account = new Account() { Code = _connector.AccountCode};

            var resolveResult = new TaskCompletionSource<Account>();

            var updateAccountTime = new Action<DateTime>(time => account.Time = time);
            var updateAccountValue = new Action<string, string, string>((key, value, currency) =>
            {
                switch(key)
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
            var updatePortfolio = new Action<Position>(pos => account.Positions.Add(pos));
            var accountDownloadEnd = new Action<string>(accountCode => resolveResult.SetResult(account));
            var error = new Action<ClientMessage>(msg => TaskError(msg, resolveResult));

            _updateAccountTimeEvent += updateAccountTime;
            AccountValueUpdated += updateAccountValue;
            _updatePortfolioEvent += updatePortfolio;
            _accountDownloadEndEvent += accountDownloadEnd;
            ClientMessageReceived += error;

            resolveResult.Task.ContinueWith(t =>
            {
                _updateAccountTimeEvent -= updateAccountTime;
                AccountValueUpdated -= updateAccountValue;
                _updatePortfolioEvent -= updatePortfolio;
                _accountDownloadEndEvent -= accountDownloadEnd;
                ClientMessageReceived -= error;

                if(!receiveUpdates)
                    _connector.ClientSocket.reqAccountUpdates(false, _connector.AccountCode);
            });

            _connector.ClientSocket.reqAccountUpdates(true, _connector.AccountCode);

            return resolveResult.Task;
        }

        public void CancelAccountUpdates() => _connector.ClientSocket.reqAccountUpdates(false, _connector.AccountCode);

        public void updateAccountTime(string timestamp)
        {
            _logger.LogDebug($"Getting account time : {timestamp}");
            _updateAccountTimeEvent?.Invoke(DateTime.Parse(timestamp, CultureInfo.InvariantCulture));
        }

        public void updateAccountValue(string key, string value, string currency, string accountName)
        {
            _logger.LogDebug($"account value : {key} {value} {currency}");

            switch (key)
            {
                case "AccountReady":
                    if (!bool.Parse(value))
                    {
                        string msg = "Account not available at the moment. The IB server is in the process of resetting. Values returned may not be accurate. Try again later";
                        error(msg);
                    }
                    break;

                default:
                    AccountValueUpdated?.Invoke(key, value, currency);
                    break;
            }
        }
        
        public void updatePortfolio(IBApi.Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName)
        {
            var pos = new Position()
            {
                Contract = contract.ToTBContract(),
                PositionAmount = position,
                MarketPrice = marketPrice,
                MarketValue = marketValue,
                AverageCost = averageCost,
                UnrealizedPNL = unrealizedPNL,
                RealizedPNL = realizedPNL,
            };

            _logger.LogDebug($"Getting portfolio : \n{pos}");

            _updatePortfolioEvent?.Invoke(pos);
        }

        public void accountDownloadEnd(string account)
        {
            _accountDownloadEndEvent?.Invoke(account);
        }

        public void RequestPositions()
        {
            _logger.LogDebug($"Requesting positions for all accounts");
            _connector.ClientSocket.reqPositions();
        }

        public void position(string account, IBApi.Contract contract, double pos, double avgCost)
        {
            if(account == _connector.AccountCode)
            {
                var p = new Position()
                {
                    Contract = contract.ToTBContract(),
                    PositionAmount = pos,
                    AverageCost = avgCost,
                };
                
                _logger.LogDebug($"position received for account {account} : contract={p.Contract} pos={pos} avgCost={avgCost}");
                PositionReceived?.Invoke(p);
            }
            else
                _logger.LogDebug($"position ignored for account {account}");
        }

        public void positionEnd() => _logger.LogDebug($"positionEnd");

        public void CancelPositionsSubscription()
        {
            _connector.ClientSocket.cancelPositions();
            _logger.LogDebug($"cancelPositions");
        }

        public void RequestPnL(Contract contract)
        {
            if (PnlSubscriptions.ContainsKey(contract))
                return;

            _logger.LogDebug($"Requesting PnL for {contract}");
            int reqId = _connector.NextRequestId;
            PnlSubscriptions[contract] = reqId;
            _connector.ClientSocket.reqPnLSingle(reqId, _connector.AccountCode, "", contract.Id);
        }

        public void pnlSingle(int reqId, int pos, double dailyPnL, double unrealizedPnL, double realizedPnL, double value)
        {
            var pnl = new PnL()
            {
                Contract = PnlSubscriptions.First(s => s.Value == reqId).Key,
                PositionAmount = pos,
                MarketValue = value,  
                DailyPnL = dailyPnL,
                UnrealizedPnL = unrealizedPnL,
                RealizedPnL = realizedPnL
            };

            _logger.LogDebug($"PnL : {pnl}");
            PnLReceived?.Invoke(pnl);
        }

        public void CancelPnLRequest(Contract contract)
        {
            if(PnlSubscriptions.ContainsKey(contract))
            {
                _connector.ClientSocket.cancelPnL(PnlSubscriptions[contract]);
                PnlSubscriptions.Remove(contract);
                _logger.LogDebug($"CancelPnLRequest for {contract}");
            }
        }

        public void RequestFiveSecondsBars(Contract contract)
        {
            if (FiveSecSubscriptions.ContainsKey(contract))
                return;

            var ibc = contract.ToIBApiContract();
            int reqId = _connector.NextRequestId;
            FiveSecSubscriptions[contract] = reqId;

            // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
            _connector.ClientSocket.reqRealTimeBars(reqId, ibc, 5, "TRADES", true, null);
        }

        // called at 5 sec intervals
        public void realtimeBar(int reqId, long date, double open, double high, double low, double close, long volume, double WAP, int count)
        {
            var contract = FiveSecSubscriptions.First(c => c.Value == reqId).Key;

            FiveSecBarReceived?.Invoke(contract, new MarketData.Bar()
            {
                BarLength = BarLength._5Sec,
                Open = open,
                Close = close,
                High = high,
                Low = low,
                Volume = volume,
                TradeAmount = count,
                Time = DateTimeOffset.FromUnixTimeSeconds(date).DateTime.ToLocalTime(),
            });
        }

        public void CancelFiveSecondsBarsRequest(Contract contract)
        {
            if(FiveSecSubscriptions.ContainsKey(contract))
            {
                _connector.ClientSocket.cancelRealTimeBars(FiveSecSubscriptions[contract]);
                FiveSecSubscriptions.Remove(contract);
            }
        }

        public void RequestBidAsk(Contract contract)
        {
            if (BidAskSubscriptions.ContainsKey(contract))
                return;

            var ibc = contract.ToIBApiContract();
            int reqId = _connector.NextRequestId;
            BidAskSubscriptions[contract] = reqId;

            // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
            _connector.ClientSocket.reqTickByTickData(reqId, ibc, "BidAsk", 0, false);
        }

        public void tickByTickBidAsk(int reqId, long time, double bidPrice, double askPrice, int bidSize, int askSize, TickAttribBidAsk tickAttribBidAsk)
        {
            var contract = BidAskSubscriptions.First(c => c.Value == reqId).Key;

            _bidAsk.Bid = bidPrice;
            _bidAsk.BidSize = bidSize;
            _bidAsk.Ask = askPrice;
            _bidAsk.AskSize = askSize;
            _bidAsk.Time = DateTimeOffset.FromUnixTimeSeconds(time).DateTime.ToLocalTime();

            BidAskReceived?.Invoke(contract, _bidAsk);
        }

        public void CancelBidAskRequest(Contract contract)
        {
            if (BidAskSubscriptions.ContainsKey(contract))
            {
                _connector.ClientSocket.cancelTickByTickData(BidAskSubscriptions[contract]);
                BidAskSubscriptions.Remove(contract);
            }
        }

        public Task<List<Contract>> GetContractsAsync(string ticker)
        {
            var sampleContract = new IBApi.Contract()
            {
                Currency = "USD",
                Exchange = "SMART",
                Symbol = ticker,
                SecType = "STK"
            };
            var reqId = _connector.NextRequestId;

            var resolveResult = new TaskCompletionSource<List<Contract>>();
            var error = new Action<ClientMessage>(msg => TaskError(msg, resolveResult));
            var tmpContracts = new List<Contract>();
            var contractDetails = new Action<int, Contract>((rId, c) => 
            { 
                if(rId == reqId)
                    tmpContracts.Add(c);
            });
            var contractDetailsEnd = new Action<int>(rId => 
            {
                if(rId == reqId)
                    resolveResult.SetResult(tmpContracts);
            });

            _contractDetailsEvent += contractDetails;
            _contractDetailsEndEvent += contractDetailsEnd;
            ClientMessageReceived += error;

            resolveResult.Task.ContinueWith(t =>
            {
                _contractDetailsEvent -= contractDetails;
                _contractDetailsEndEvent -= contractDetailsEnd;
                ClientMessageReceived -= error;
            });

            _connector.ClientSocket.reqContractDetails(reqId, sampleContract);

            return resolveResult.Task;
        }

        public void contractDetails(int reqId, ContractDetails contractDetails)
        {
            _contractDetailsEvent?.Invoke(reqId, contractDetails.Contract.ToTBContract());
        }

        public void contractDetailsEnd(int reqId)
        {
            _contractDetailsEndEvent?.Invoke(reqId);    
        }

        public void RequestOpenOrders() => _connector.ClientSocket.reqOpenOrders();

        public void openOrderEnd() => _logger.LogDebug($"openOrderEnd");

        public void PlaceOrder(Contract contract, TBOrder order)
        {
            var ibo = order.ToIBApiOrder();
            _connector.ClientSocket.placeOrder(ibo.OrderId, contract.ToIBApiContract(), ibo);
        }

        public void openOrder(int orderId, IBApi.Contract contract, IBApi.Order order, IBApi.OrderState orderState)
        {
            // orderState only used then IBApi.Order.WhatIf = true ?? 
            _logger.LogDebug($"openOrder {orderId} : {orderState.Status}");
            OrderOpened?.Invoke(contract.ToTBContract(), order.ToTBOrder(), orderState.ToTBOrderState());
        }

        public void orderStatus(int orderId, string status, double filled, double remaining, double avgFillPrice, int permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice)
        {
            _logger.LogDebug($"orderStatus {orderId} : status={status} filled={filled} remaining={remaining} avgFillprice={avgFillPrice} avgFillPrice={lastFillPrice}");

            var os = new OrderStatus()
            {
                 Info = new RequestInfo()
                 {
                     OrderId = orderId,
                     ParentId = parentId,
                     ClientId = clientId,
                     PermId = permId,
                 },
                  Status = !string.IsNullOrEmpty(status) ? (Status)Enum.Parse(typeof(Status), status) : Status.Unknown,
                  Filled = filled,
                  Remaining = remaining,
                  AvgFillPrice = avgFillPrice,
                  LastFillPrice = lastFillPrice,
                  MktCapPrice = mktCapPrice,
            };

            OrderStatusChanged?.Invoke(os);
        }

        public void CancelOrder(int orderId) => _connector.ClientSocket.cancelOrder(orderId);
        public void CancelAllOrders() => _connector.ClientSocket.reqGlobalCancel();

        public void execDetails(int reqId, IBApi.Contract contract, Execution execution)
        {
            _logger.LogDebug($"execDetails : reqId={reqId}");
            OrderExecuted?.Invoke(contract.ToTBContract(), execution.ToTBExecution());
        }

        public void execDetailsEnd(int reqId)
        => _logger.LogDebug($"execDetailsEnd : reqId={reqId}");

        public void commissionReport(CommissionReport commissionReport)
        {
            _logger.LogDebug($"commissionReport : commission={commissionReport.Commission} Currency={commissionReport.Currency} RealizedPNL={commissionReport.RealizedPNL}");
            CommissionInfoReceived?.Invoke(commissionReport.ToTBCommission());
        }

        public void completedOrder(IBApi.Contract contract, IBApi.Order order, IBApi.OrderState orderState)
        => _logger.LogDebug($"completedOrder {order.OrderId} : {orderState.Status}");

        public void completedOrdersEnd()
        => _logger.LogDebug($"completedOrdersEnd");

        public Task<List<MarketData.Bar>> GetHistoricalDataAsync(Contract contract, BarLength barLength, int count)
        {
            return GetHistoricalDataAsync(contract, barLength, string.Empty, count);
        }

        public Task<List<MarketData.Bar>> GetHistoricalDataAsync(Contract contract, BarLength barLength, string endDateTime, int count)
        {
            var tmpList = new List<MarketData.Bar>();
            var reqId = _connector.NextRequestId;

            var resolveResult = new TaskCompletionSource<List<MarketData.Bar>>();
            SetupHistoricalDataCallbacks(tmpList, reqId, resolveResult);

            //string timeFormat = "yyyyMMdd-HH:mm:ss";
            string durationStr = null;
            string barSizeStr = null;
            switch (barLength)
            {
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

            _connector.ClientSocket.reqHistoricalData(reqId, contract.ToIBApiContract(), endDateTime, durationStr, barSizeStr, "TRADES", 0, 1, false, null);

            return resolveResult.Task;
        }

#if BACKTESTING
        public Task<List<MarketData.Bar>> GetHistoricalDataForDayAsync(Contract contract, DateTime endDateTime)
        {
            var tmpList = new List<MarketData.Bar>();
            var reqId = NextRequestId;

            var resolveResult = new TaskCompletionSource<List<MarketData.Bar>>();
            SetupHistoricalDataCallbacks(tmpList, reqId, resolveResult);
            _clientSocket.reqHistoricalData(reqId, contract.ToIBApiContract(), endDateTime.ToString("yyyyMMdd-HH:mm:ss"), "1 D", "1 min", "TRADES", 0, 1, false, null);
            return resolveResult.Task;
        }
#endif
        private void SetupHistoricalDataCallbacks(List<MarketData.Bar> tmpList, int reqId, TaskCompletionSource<List<MarketData.Bar>> resolveResult)
        {
            var historicalData = new Action<int, MarketData.Bar>((rId, bar) =>
            {
                if (rId == reqId)
                {
                    tmpList.Add(bar);
                }
            });

            var historicalDataEnd = new Action<int, string, string>((rId, start, end) =>
            {
                if (rId == reqId)
                {
                    resolveResult.SetResult(tmpList);
                }
            });

            var error = new Action<ClientMessage>(msg => TaskError(msg, resolveResult));

            _historicalDataEvent += historicalData;
            _historicalDataEndEvent += historicalDataEnd;
            ClientMessageReceived += error;

            resolveResult.Task.ContinueWith(t =>
            {
                _historicalDataEvent -= historicalData;
                _historicalDataEndEvent -= historicalDataEnd;
                ClientMessageReceived -= error;
            });
        }

        public void historicalData(int reqId, IBApi.Bar bar)
        {
            _historicalDataEvent?.Invoke(reqId, new MarketData.Bar()
            {
                Open = bar.Open,
                Close = bar.Close,
                High = bar.High,
                Low = bar.Low,
                Volume = bar.Volume,
                TradeAmount = bar.Count,
                
                // non-standard date format...
                Time = DateTime.ParseExact(bar.Time, "yyyyMMdd  HH:mm:ss", CultureInfo.InvariantCulture)
            });
        }

        public void historicalDataEnd(int reqId, string start, string end)
        {
            _historicalDataEndEvent?.Invoke(reqId, start, end);
        }

        public void error(Exception e) => _connector.error(e);
        public void error(string str) => _connector.error(str);
        public void error(int id, int errorCode, string errorMsg) => _connector.error(id, errorCode, errorMsg);
        void TaskError<T>(ClientMessage msg, TaskCompletionSource<T> resolveResult) => _connector.TaskError(msg, resolveResult);

        #region Not implemented

        public void accountSummary(int reqId, string account, string tag, string value, string currency)
        {
            throw new NotImplementedException();
        }

        public void accountSummaryEnd(int reqId)
        {
            throw new NotImplementedException();
        }

        public void accountUpdateMulti(int requestId, string account, string modelCode, string key, string value, string currency)
        {
            throw new NotImplementedException();
        }

        public void accountUpdateMultiEnd(int requestId)
        {
            throw new NotImplementedException();
        }

        public void bondContractDetails(int reqId, ContractDetails contract)
        {
            throw new NotImplementedException();
        }

        public void currentTime(long time)
        {
            throw new NotImplementedException();
        }

        public void deltaNeutralValidation(int reqId, DeltaNeutralContract deltaNeutralContract)
        {
            throw new NotImplementedException();
        }

        public void displayGroupList(int reqId, string groups)
        {
            throw new NotImplementedException();
        }

        public void displayGroupUpdated(int reqId, string contractInfo)
        {
            throw new NotImplementedException();
        }

        public void familyCodes(FamilyCode[] familyCodes)
        {
            throw new NotImplementedException();
        }

        public void fundamentalData(int reqId, string data)
        {
            throw new NotImplementedException();
        }

        public void headTimestamp(int reqId, string headTimestamp)
        {
            throw new NotImplementedException();
        }

        public void histogramData(int reqId, HistogramEntry[] data)
        {
            throw new NotImplementedException();
        }

        public void historicalDataUpdate(int reqId, IBApi.Bar bar)
        {
            throw new NotImplementedException();
        }

        public void historicalNews(int requestId, string time, string providerCode, string articleId, string headline)
        {
            throw new NotImplementedException();
        }

        public void historicalNewsEnd(int requestId, bool hasMore)
        {
            throw new NotImplementedException();
        }

        public void historicalTicks(int reqId, HistoricalTick[] ticks, bool done)
        {
            throw new NotImplementedException();
        }

        public void historicalTicksBidAsk(int reqId, HistoricalTickBidAsk[] ticks, bool done)
        {
            throw new NotImplementedException();
        }

        public void historicalTicksLast(int reqId, HistoricalTickLast[] ticks, bool done)
        {
            throw new NotImplementedException();
        }

        public void marketDataType(int reqId, int marketDataType)
        {
            throw new NotImplementedException();
        }

        public void marketRule(int marketRuleId, PriceIncrement[] priceIncrements)
        {
            throw new NotImplementedException();
        }

        public void mktDepthExchanges(DepthMktDataDescription[] depthMktDataDescriptions)
        {
            throw new NotImplementedException();
        }

        public void newsArticle(int requestId, int articleType, string articleText)
        {
            throw new NotImplementedException();
        }

        public void newsProviders(NewsProvider[] newsProviders)
        {
            throw new NotImplementedException();
        }
        public void orderBound(long orderId, int apiClientId, int apiOrderId)
        {
            throw new NotImplementedException();
        }

        public void pnl(int reqId, double dailyPnL, double unrealizedPnL, double realizedPnL)
        {
            throw new NotImplementedException();
        }

        public void positionMulti(int requestId, string account, string modelCode, IBApi.Contract contract, double pos, double avgCost)
        {
            throw new NotImplementedException();
        }

        public void positionMultiEnd(int requestId)
        {
            throw new NotImplementedException();
        }

        public void receiveFA(int faDataType, string faXmlData)
        {
            throw new NotImplementedException();
        }

        public void replaceFAEnd(int reqId, string text)
        {
            throw new NotImplementedException();
        }

        public void rerouteMktDataReq(int reqId, int conId, string exchange)
        {
            throw new NotImplementedException();
        }

        public void rerouteMktDepthReq(int reqId, int conId, string exchange)
        {
            throw new NotImplementedException();
        }

        public void scannerData(int reqId, int rank, ContractDetails contractDetails, string distance, string benchmark, string projection, string legsStr)
        {
            throw new NotImplementedException();
        }

        public void scannerDataEnd(int reqId)
        {
            throw new NotImplementedException();
        }

        public void scannerParameters(string xml)
        {
            throw new NotImplementedException();
        }

        public void securityDefinitionOptionParameter(int reqId, string exchange, int underlyingConId, string tradingClass, string multiplier, HashSet<string> expirations, HashSet<double> strikes)
        {
            throw new NotImplementedException();
        }

        public void securityDefinitionOptionParameterEnd(int reqId)
        {
            throw new NotImplementedException();
        }

        public void smartComponents(int reqId, Dictionary<int, KeyValuePair<string, char>> theMap)
        {
            throw new NotImplementedException();
        }

        public void softDollarTiers(int reqId, SoftDollarTier[] tiers)
        {
            throw new NotImplementedException();
        }

        public void symbolSamples(int reqId, ContractDescription[] contractDescriptions)
        {
            throw new NotImplementedException();
        }

        public void tickByTickAllLast(int reqId, int tickType, long time, double price, int size, TickAttribLast tickAttribLast, string exchange, string specialConditions)
        {
            throw new NotImplementedException();
        }

        public void tickByTickMidPoint(int reqId, long time, double midPoint)
        {
            throw new NotImplementedException();
        }

        public void tickEFP(int tickerId, int tickType, double basisPoints, string formattedBasisPoints, double impliedFuture, int holdDays, string futureLastTradeDate, double dividendImpact, double dividendsToLastTradeDate)
        {
            throw new NotImplementedException();
        }

        public void tickGeneric(int tickerId, int field, double value)
        {
            throw new NotImplementedException();
        }

        public void tickNews(int tickerId, long timeStamp, string providerCode, string articleId, string headline, string extraData)
        {
            throw new NotImplementedException();
        }

        public void tickOptionComputation(int tickerId, int field, int tickAttrib, double impliedVolatility, double delta, double optPrice, double pvDividend, double gamma, double vega, double theta, double undPrice)
        {
            throw new NotImplementedException();
        }

        public void tickPrice(int tickerId, int field, double price, TickAttrib attribs)
        {
            throw new NotImplementedException();
        }

        public void tickReqParams(int tickerId, double minTick, string bboExchange, int snapshotPermissions)
        {
            throw new NotImplementedException();
        }

        public void tickSize(int tickerId, int field, int size)
        {
            throw new NotImplementedException();
        }

        public void tickSnapshotEnd(int tickerId)
        {
            throw new NotImplementedException();
        }

        public void tickString(int tickerId, int field, string value)
        {
            throw new NotImplementedException();
        }

        public void updateMktDepth(int tickerId, int position, int operation, int side, double price, int size)
        {
            throw new NotImplementedException();
        }

        public void updateMktDepthL2(int tickerId, int position, string marketMaker, int operation, int side, double price, int size, bool isSmartDepth)
        {
            throw new NotImplementedException();
        }

        public void updateNewsBulletin(int msgId, int msgType, string message, string origExchange)
        {
            throw new NotImplementedException();
        }

        public void verifyAndAuthCompleted(bool isSuccessful, string errorText)
        {
            throw new NotImplementedException();
        }

        public void verifyAndAuthMessageAPI(string apiData, string xyzChallenge)
        {
            throw new NotImplementedException();
        }

        public void verifyCompleted(bool isSuccessful, string errorText)
        {
            throw new NotImplementedException();
        }

        public void verifyMessageAPI(string apiData)
        {
            throw new NotImplementedException();
        }

#endregion Not implemented
    }
}
