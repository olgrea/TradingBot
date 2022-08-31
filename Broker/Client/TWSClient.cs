using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using IBApi;
using TradingBot.Utils;
using TradingBot.Broker.MarketData;
using System.Threading;
using System.Diagnostics;
using TradingBot.Broker.Orders;

using TBOrder = TradingBot.Broker.Orders.Order;
using TBOrderState = TradingBot.Broker.Orders.OrderState;

namespace TradingBot.Broker.Client
{
    internal partial class TWSClient : EWrapper
    {
        int _nextValidOrderId = -1;
        int _reqId = 0;

        int _clientId = -1;
        EClientSocket _clientSocket;
        EReaderSignal _signal;
        EReader _reader;

        ILogger _logger;
        Task _processMsgTask;

        string _accountCode = null;
        Account _account = new Account();

        public Action<Contract, BidAsk> BidAskReceived;
        Dictionary<Contract, int> _bidAskSubscriptions = new Dictionary<Contract, int>();
        MarketData.BidAsk _bidAsk = new BidAsk();

        public Action<Contract, MarketData.Bar> FiveSecBarReceived;
        Dictionary<Contract, int> _fiveSecSubscriptions = new Dictionary<Contract, int>();

        AutoResetEvent _autoResetEvent = new AutoResetEvent(false);
        Contract _contract;

        public Action<Contract, List<TBOrder>, TBOrderState> OrdersOpened;

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
        
        public Account GetAccount()
        {
            _clientSocket.reqAccountUpdates(true, _accountCode);
            _account.Code = _accountCode;
            _autoResetEvent.WaitOne();
            return _account;
        }

        public void updateAccountTime(string timestamp)
        {
            _logger.LogDebug($"Getting account time : {timestamp}");
            _account.Time = DateTime.Parse(timestamp);
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
                    _account.CashBalances.Add(new CashBalance(decimal.Parse(value, CultureInfo.InvariantCulture), currency));
                    break;

                case "RealizedPnL":
                    if (currency == "USD")
                        _account.RealizedPnL = new CashBalance(decimal.Parse(value, CultureInfo.InvariantCulture), currency);
                    break;

                case "UnrealizedPnL":
                    if(currency == "USD")
                        _account.UnrealizedPnL = new CashBalance(decimal.Parse(value, CultureInfo.InvariantCulture), currency);
                    break;
            }

        }
        
        public void updatePortfolio(IBApi.Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName)
        {
            var pos = new Position()
            {
                Contract = contract.ToTBContract(),
                PositionAmount = Convert.ToDecimal(position),
                MarketPrice = Convert.ToDecimal(marketPrice),
                MarketValue = Convert.ToDecimal(marketValue),
                AverageCost = Convert.ToDecimal(averageCost),
                UnrealizedPNL = Convert.ToDecimal(unrealizedPNL),
                RealizedPNL = Convert.ToDecimal(realizedPNL),
            };

            _logger.LogDebug($"Getting portfolio : \n{pos}");

            _account.Positions.Add(pos);
        }

        public void accountDownloadEnd(string account)
        {
            _autoResetEvent.Set();
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
                Open = Convert.ToDecimal(open),
                Close = Convert.ToDecimal(close),
                High = Convert.ToDecimal(high),
                Low = Convert.ToDecimal(low),
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

            _bidAsk.Bid = Convert.ToDecimal(bidPrice);
            _bidAsk.BidSize = bidSize;
            _bidAsk.Ask = Convert.ToDecimal(askPrice);
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

        List<TBOrder> AssignOrderIdsAndFlatten(TBOrder order, List<TBOrder> list = null)
        {
            if (order == null) 
                return null;

            list ??= new List<TBOrder>();

            order.Request.OrderId = GetNextValidOrderId();
            order.Request.Transmit = false;
            list.Add(order);

            if(order.Request.AttachedOrders.Any())
            {
                int parentId = order.Request.OrderId;
                
                int count = order.Request.AttachedOrders.Count;
                for (int i = 0; i < count; i++)
                {
                    var child = order.Request.AttachedOrders[i];
                    child.Request.ParentId = parentId;
                    AssignOrderIdsAndFlatten(child, list);
                }
            }

            return list;
        }

        public void PlaceOrder(Contract contract, TBOrder order)
        {
            if (contract == null || order == null)
                return;

            var list = AssignOrderIdsAndFlatten(order);

            // only the last child must be set to true
            list.Last().Request.Transmit = true;

            foreach(var o in list)
            {
                var ibo = o.ToIBApiOrder();
                Debug.Assert(ibo.OrderId > 0);
                _clientSocket.placeOrder(ibo.OrderId, contract.ToIBApiContract(), ibo);
            }
        }

        public void openOrder(int orderId, IBApi.Contract contract, IBApi.Order order, IBApi.OrderState orderState)
        {
            // orderState only used then IBApi.Order.WhatIf = true ?? 

            // TODO test multiple sent order
            _logger.LogDebug($"openOrder {orderId} : {orderState.Status}");
        }

        public void openOrderEnd()
        {
            _logger.LogDebug($"openOrderEnd");
        }

        public void orderBound(long orderId, int apiClientId, int apiOrderId)
        {
            _logger.LogDebug($"orderBound : orderId={orderId} apiClientId={apiClientId} apiOrderId={apiOrderId}");
        }

        public void orderStatus(int orderId, string status, double filled, double remaining, double avgFillPrice, int permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice)
        {
            _logger.LogDebug($"orderStatus {orderId} : status={status} filled={filled} remaining={remaining} avgFillprice={avgFillPrice} avgFillPrice={lastFillPrice}");
        }

        public void execDetails(int reqId, IBApi.Contract contract, Execution execution)
        {
            _logger.LogDebug($"execDetails : reqId={reqId}");
        }

        public void execDetailsEnd(int reqId)
        {
            _logger.LogDebug($"execDetailsEnd : reqId={reqId}");
        }

        public void commissionReport(CommissionReport commissionReport)
        {
            _logger.LogDebug($"commissionReport : commission={commissionReport.Commission} Currency={commissionReport.Currency} RealizedPNL={commissionReport.RealizedPNL}");
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
            throw e;
        }

        public void error(string str)
        {
            _logger.LogError(str);
        }

        public void error(int id, int errorCode, string errorMsg)
        {
            var str = $"{id} {errorCode} {errorMsg}";
            if (errorCode == 502)
                str += $"\nMake sure the API is enabled in Trader Workstation";

            if (errorCode < 0)
                _logger.LogDebug(str);
            else
                _logger.LogError(str);
        }
    }

    internal partial class TWSClient : EWrapper
    {
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

        public void pnl(int reqId, double dailyPnL, double unrealizedPnL, double realizedPnL)
        {
            throw new NotImplementedException();
        }

        public void pnlSingle(int reqId, int pos, double dailyPnL, double unrealizedPnL, double realizedPnL, double value)
        {
            throw new NotImplementedException();
        }

        public void position(string account, IBApi.Contract contract, double pos, double avgCost)
        {
            throw new NotImplementedException();
        }

        public void positionEnd()
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

    }
}
