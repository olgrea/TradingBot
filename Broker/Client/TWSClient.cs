using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using IBApi;
using TradingBot.Utils;
using TradingBot.Broker.MarketData;
using System.Threading;
using TradingBot.Broker.Orders;

using TBOrder = TradingBot.Broker.Orders.Order;
using TBOrderState = TradingBot.Broker.Orders.OrderState;
using TradingBot.Broker.Accounts;

namespace TradingBot.Broker.Client
{
    public class TWSClient : EWrapper
    {
        int _nextValidOrderId = -1;
        int _reqId = 0;

        int _clientId = -1;
        EClientSocket _clientSocket;
        EReaderSignal _signal;
        EReader _reader;

        ILogger _logger;
        Task _processMsgTask;

        Dictionary<Contract, int> _bidAskSubscriptions = new Dictionary<Contract, int>();
        Dictionary<Contract, int> _fiveSecSubscriptions = new Dictionary<Contract, int>();
        Dictionary<Contract, int> _pnlSubscriptions = new Dictionary<Contract, int>();
        
        string _accountCode = null;
        Accounts.Account _account = new Accounts.Account();
        MarketData.BidAsk _bidAsk = new BidAsk();
        AutoResetEvent _autoResetEvent = new AutoResetEvent(false);
        Contract _contract;

        //TODO : refactor to async/await model and remove AutoResetEvent

        public event Action<PnL> PnLReceived;
        public event Action<Position> PositionReceived;

        public event Action<Contract, MarketData.Bar> FiveSecBarReceived;
        public event Action<Contract, BidAsk> BidAskReceived;
        public event Action<Contract, TBOrder, TBOrderState> OrderOpened;
        public event Action<OrderStatus> OrderStatusChanged;
        public event Action<Contract, OrderExecution> OrderExecuted;
        public event Action<CommissionInfo> CommissionInfoReceived;
        public event Action<ClientMessage> ClientMessageReceived;

        public TWSClient(ILogger logger)
        {
            _signal = new EReaderMonitorSignal();
            _clientSocket = new EClientSocket(this, _signal);
            _logger = logger;
        }

        int NextRequestId => _reqId++;
        int NextValidOrderId => _nextValidOrderId++;

        public void Connect(string host, int port, int clientId)
        {
            //TODO: Handle IB server resets

            if (_clientId >= 0)
                throw new NotImplementedException("Implement Next Valid Identifier logic with IBApi.EClient.reqIds ");

            _clientId = clientId;
            _clientSocket.eConnect(host, port, clientId);
            _reader = new EReader(_clientSocket, _signal);
            _reader.Start();
            _processMsgTask = Task.Run(ProcessMsg);
            _logger.LogDebug($"Reader started and is listening to messages from TWS");
            _autoResetEvent.WaitOne();
        }

        public void Disconnect()
        {
            _clientSocket.reqAccountUpdates(false, _accountCode);
            foreach (var kvp in _bidAskSubscriptions)
                _clientSocket.cancelTickByTickData(kvp.Value);

            foreach (var kvp in _fiveSecSubscriptions)
                _clientSocket.cancelRealTimeBars(kvp.Value);

            _clientSocket.eDisconnect();
            _clientId = -1;
        }

        public bool IsConnected => _clientSocket.IsConnected() && _nextValidOrderId > 0;

        void ProcessMsg()
        {
            while (_clientSocket.IsConnected())
            {
                _signal.waitForSignal();
                _reader.processMsgs();
            }
        }

        public void connectAck()
        {
            _logger.LogDebug($"Connecting client to TWS...");
        }

        public void connectionClosed()
        {
            _logger.LogDebug($"Connection closed");
        }

        public void managedAccounts(string accountsList)
        {
            _logger.LogDebug($"Account list : {accountsList}");
            _accountCode = accountsList;
        }

        public int GetNextValidOrderId(bool fromTWS = false)
        {
            if(fromTWS)
            {
                _clientSocket.reqIds(-1); // param is deprecated
                _autoResetEvent.WaitOne();
            }
            return NextValidOrderId;   
        }

        // The next valid identifier is persistent between TWS sessions
        public void nextValidId(int orderId)
        {
            if (_nextValidOrderId < 0)
            {
                _logger.LogInfo($"Client connected.");
            }

            _nextValidOrderId = orderId;
            _autoResetEvent.Set();
        }
        
        public Accounts.Account GetAccount(bool receiveUpdates = true)
        {
            _clientSocket.reqAccountUpdates(receiveUpdates, _accountCode);
            _account.Code = _accountCode;

            // TODO : make sure that errors don't cause blocking... 
            // Maybe it's better to just keep this async and use callbacks
            _autoResetEvent.WaitOne();

            return _account;
        }

        public void updateAccountTime(string timestamp)
        {
            _logger.LogDebug($"Getting account time : {timestamp}");
            _account.Time = DateTime.Parse(timestamp, CultureInfo.InvariantCulture);
        }

        public void updateAccountValue(string key, string value, string currency, string accountName)
        {
            _logger.LogDebug($"account value : {key} {value} {currency}");

            switch (key)
            {
                case "AccountReady":
                    if (!bool.Parse(value))
                        _logger.LogError("Account not available at the moment. The IB server is in the process of resetting. Values returned may not be accurate. Try again later");
                    break;

                case "CashBalance":
                    _account.CashBalances[currency] = double.Parse(value, CultureInfo.InvariantCulture);
                    break;

                case "RealizedPnL":
                    _account.RealizedPnL[currency] = double.Parse(value, CultureInfo.InvariantCulture);
                    break;

                case "UnrealizedPnL":
                    _account.UnrealizedPnL[currency] = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
            }
        }
        
        public void updatePortfolio(IBApi.Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName)
        {
            //TODO : garbage cleanup

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

            _account.Positions[pos.Contract] = pos;
        }

        public void accountDownloadEnd(string account)
        {
            _autoResetEvent.Set();
        }

        public void RequestPositions()
        {
            _logger.LogDebug($"Requesting positions for all accounts");
            _clientSocket.reqPositions();
        }

        public void position(string account, IBApi.Contract contract, double pos, double avgCost)
        {
            if(account == _accountCode)
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

        public void positionEnd()
        {
            _logger.LogDebug($"positionEnd");
        }

        public void CancelPositionsSubscription()
        {
            _clientSocket.cancelPositions();
            _logger.LogDebug($"cancelPositions");
        }

        public void RequestPnL(Contract contract)
        {
            if (_pnlSubscriptions.ContainsKey(contract))
                return;

            _logger.LogDebug($"Requesting PnL for {contract}");
            int reqId = NextRequestId;
            _pnlSubscriptions[contract] = reqId;
            _clientSocket.reqPnLSingle(reqId, _accountCode, "", contract.Id);
        }

        public void pnlSingle(int reqId, int pos, double dailyPnL, double unrealizedPnL, double realizedPnL, double value)
        {
            var pnl = new PnL()
            {
                Contract = _pnlSubscriptions.First(s => s.Value == reqId).Key,
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
            if(_pnlSubscriptions.ContainsKey(contract))
            {
                _clientSocket.cancelPnL(_pnlSubscriptions[contract]);
                _pnlSubscriptions.Remove(contract);
                _logger.LogDebug($"CancelPnLRequest for {contract}");
            }
        }

        public void RequestFiveSecondsBars(Contract contract)
        {
            if (_fiveSecSubscriptions.ContainsKey(contract))
                return;

            var ibc = contract.ToIBApiContract();
            int reqId = NextRequestId;
            _fiveSecSubscriptions[contract] = reqId;

            // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
            _clientSocket.reqRealTimeBars(reqId, ibc, 5, "TRADES", true, null);
        }

        // called at 5 sec intervals
        public void realtimeBar(int reqId, long date, double open, double high, double low, double close, long volume, double WAP, int count)
        {
            var contract = _fiveSecSubscriptions.First(c => c.Value == reqId).Key;

            FiveSecBarReceived?.Invoke(contract, new MarketData.Bar()
            {
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
            if(_fiveSecSubscriptions.ContainsKey(contract))
            {
                _clientSocket.cancelRealTimeBars(_fiveSecSubscriptions[contract]);
                _fiveSecSubscriptions.Remove(contract);
            }
        }

        public void RequestBidAsk(Contract contract)
        {
            if (_bidAskSubscriptions.ContainsKey(contract))
                return;

            var ibc = contract.ToIBApiContract();
            int reqId = NextRequestId;
            _bidAskSubscriptions[contract] = reqId;

            // TODO : "It may be necessary to remake real time bars subscriptions after the IB server reset or between trading sessions."
            _clientSocket.reqTickByTickData(reqId, ibc, "BidAsk", 0, false);
        }

        public void tickByTickBidAsk(int reqId, long time, double bidPrice, double askPrice, int bidSize, int askSize, TickAttribBidAsk tickAttribBidAsk)
        {
            var contract = _bidAskSubscriptions.First(c => c.Value == reqId).Key;

            _bidAsk.Bid = bidPrice;
            _bidAsk.BidSize = bidSize;
            _bidAsk.Ask = askPrice;
            _bidAsk.AskSize = askSize;
            _bidAsk.Time = DateTimeOffset.FromUnixTimeSeconds(time).DateTime.ToLocalTime();

            BidAskReceived?.Invoke(contract, _bidAsk);
        }

        public void CancelBidAskRequest(Contract contract)
        {
            if (_bidAskSubscriptions.ContainsKey(contract))
            {
                _clientSocket.cancelTickByTickData(_bidAskSubscriptions[contract]);
                _bidAskSubscriptions.Remove(contract);
            }
        }

        public Contract GetContract(string ticker)
        {
            var sampleContract = new IBApi.Contract()
            {
                Currency = "USD",
                Exchange = "SMART",
                Symbol = ticker,
                SecType = "STK"
            };

            _clientSocket.reqContractDetails(NextRequestId, sampleContract);

            // TODO : make sure that errors don't cause blocking... 
            // make that properly async using async/await
            _autoResetEvent.WaitOne();

            return _contract;
        }

        public void contractDetails(int reqId, ContractDetails contractDetails)
        {
            _contract = contractDetails.Contract.ToTBContract();
        }

        public void contractDetailsEnd(int reqId)
        {
            _autoResetEvent.Set();
        }

        public void RequestOpenOrders()
        {
            _clientSocket.reqOpenOrders();
        }

        public void openOrderEnd()
        {
            _logger.LogDebug($"openOrderEnd");
        }

        public void PlaceOrder(Contract contract, TBOrder order)
        {
            var ibo = order.ToIBApiOrder();
            _clientSocket.placeOrder(ibo.OrderId, contract.ToIBApiContract(), ibo);
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

        public void execDetails(int reqId, IBApi.Contract contract, Execution execution)
        {
            _logger.LogDebug($"execDetails : reqId={reqId}");
            OrderExecuted?.Invoke(contract.ToTBContract(), execution.ToTBExecution());
        }

        public void execDetailsEnd(int reqId)
        {
            _logger.LogDebug($"execDetailsEnd : reqId={reqId}");
        }

        public void commissionReport(CommissionReport commissionReport)
        {
            _logger.LogDebug($"commissionReport : commission={commissionReport.Commission} Currency={commissionReport.Currency} RealizedPNL={commissionReport.RealizedPNL}");
            CommissionInfoReceived?.Invoke(commissionReport.ToTBCommission());
        }

        public void completedOrder(IBApi.Contract contract, IBApi.Order order, IBApi.OrderState orderState)
        {
            _logger.LogDebug($"completedOrder {order.OrderId} : {orderState.Status}");
        }

        public void completedOrdersEnd()
        {
            _logger.LogDebug($"completedOrdersEnd");
        }

        public void CancelOrder(int orderId)
        {
            _clientSocket.cancelOrder(orderId);
        }

        public void CancelAllOrders()
        {
            _clientSocket.reqGlobalCancel();
        }

        public void error(Exception e)
        {
            _logger.LogError(e.Message);
            ClientMessageReceived?.Invoke(new ClientException(e));
        }

        public void error(string str)
        {
            _logger.LogError(str);
            ClientMessageReceived?.Invoke(new ClientNotification(str));
        }

        public void error(int id, int errorCode, string errorMsg)
        {
            var str = $"{id} {errorCode} {errorMsg}";
            if (errorCode == 502)
                str += $"\nMake sure the API is enabled in Trader Workstation";


            // Note: id == -1 indicates a notification and not true error condition...
            ClientMessage msg;
            if (errorCode < 0)
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

        public void historicalData(int reqId, IBApi.Bar bar)
        {
            throw new NotImplementedException();
        }

        public void historicalDataEnd(int reqId, string start, string end)
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
