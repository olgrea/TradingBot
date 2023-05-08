using System.Globalization;
using IBApi;
using NLog;
using static TradingBotV2.IBKR.Client.IBClient;

namespace TradingBotV2.IBKR.Client
{
    // NOTE : TWS API uses double.MaxValue for an unset value
    public class IBResponses : EWrapper
    {
        RequestIdsToContracts _requestIdsToContracts;
        ILogger? _logger;

        internal IBResponses(RequestIdsToContracts requestIdsToContracts, ILogger? logger = null)
        {
            _logger = logger;
            _requestIdsToContracts = requestIdsToContracts;
        }

        public Action? ConnectAck;
        public void connectAck()
        {
            //_logger.Trace($"Connecting client to TWS...");
            ConnectAck?.Invoke();
        }

        public Action? ConnectionClosed;
        public void connectionClosed()
        {
            //_logger.Trace($"Connection closed");
            ConnectionClosed?.Invoke();
        }

        public Action<IEnumerable<string>>? ManagedAccounts;
        public void managedAccounts(string accountsList)
        {
            //_logger.Trace($"Account list : {accountsList}");
            var accounts = accountsList.Split(',');
            ManagedAccounts?.Invoke(accounts);
        }

        public Action<int>? NextValidId;
        public void nextValidId(int orderId)
        {
            //_logger.Trace($"NextValidId : {orderId}");
            NextValidId?.Invoke(orderId);
        }

        public Action<DateTime>? UpdateAccountTime;
        public void updateAccountTime(string timestamp)
        {
            //_logger.Trace($"Getting account time : {timestamp}");
            UpdateAccountTime?.Invoke(DateTime.Parse(timestamp, CultureInfo.InvariantCulture));
        }

        public Action<AccountValue>? UpdateAccountValue;
        public void updateAccountValue(string key, string value, string currency, string accountName)
        {
            //_logger.Trace($"account value : {key} {value} {currency}");
            UpdateAccountValue?.Invoke(new AccountValue()
            {
                Key = key,
                Value = value,
                Currency = currency,
                AccountName = accountName,
            });
        }

        public Action<Position>? UpdatePortfolio;
        public void updatePortfolio(IBApi.Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName)
        {
            var pos = new Position(contract, position, marketPrice, marketValue, averageCost, unrealizedPNL, realizedPNL);

            //_logger.Trace($"Getting portfolio : \n{pos}");
            UpdatePortfolio?.Invoke(pos);
        }

        public Action<string>? AccountDownloadEnd;
        public void accountDownloadEnd(string account)
        {
            //_logger.Trace($"accountDownloadEnd ({account})");
            AccountDownloadEnd?.Invoke(account);
        }

        public Action<int, string, string, string, string>? AccountSummary;
        public void accountSummary(int reqId, string account, string tag, string value, string currency)
        {
            //_logger.Trace($"accountSummary reqId={reqId} account={account} tag={tag} value={value} currency={currency}");
            AccountSummary?.Invoke(reqId, account, tag, value, currency);
        }

        public Action<int>? AccountSummaryEnd;
        public void accountSummaryEnd(int reqId)
        {
            //_logger.Trace($"accountSummaryEnd ({reqId})");
            AccountSummaryEnd?.Invoke(reqId);
        }

        public Action<Position>? Position;
        public void position(string account, IBApi.Contract contract, double pos, double avgCost)
        {
            var p = new Position(contract, pos, avgCost);

            //_logger.Trace($"position received for account {account} : contract={p.Contract} pos={pos} avgCost={avgCost}");
            Position?.Invoke(p);
        }

        public Action? PositionEnd;
        public void positionEnd()
        {
            //_logger.Trace($"positionEnd");
            PositionEnd?.Invoke();
        }

        public Action<PnL>? PnlSingle;
        public void pnlSingle(int reqId, int pos, double dailyPnL, double unrealizedPnL, double realizedPnL, double value)
        {
            //_logger.Trace($"PnL ({reqId}): {pnl}");
            PnlSingle?.Invoke(new PnL(_requestIdsToContracts.Pnl[reqId].Symbol, pos, dailyPnL, unrealizedPnL, realizedPnL, value));
        }

        // called at 5 sec intervals
        public Action<string, FiveSecBar>? RealtimeBar;
        public void realtimeBar(int reqId, long date, double open, double high, double low, double close, long volume, double WAP, int count)
        {
            FiveSecBar bar = new FiveSecBar()
            {
                Open = open,
                Close = close,
                High = high,
                Low = low,
                Volume = volume,
                TradeAmount = count,
                Date = date
            };

            //_logger.Trace($"realtimeBar ({reqId}) : {bar}");
            RealtimeBar?.Invoke(_requestIdsToContracts.FiveSecBars[reqId].Symbol, bar);
        }

        public Action<string, BidAsk>? TickByTickBidAsk;
        public void tickByTickBidAsk(int reqId, long time, double bidPrice, double askPrice, int bidSize, int askSize, TickAttribBidAsk tickAttribBidAsk)
        {
            var bidAsk = new BidAsk()
            {
                Time = time,
                Bid = bidPrice,
                BidSize = bidSize,
                Ask = askPrice,
                AskSize = askSize,
                TickAttribBidAsk = tickAttribBidAsk
            };

            //_logger.Trace($"tickByTickBidAsk ({reqId}) : {bidAsk}");
            TickByTickBidAsk?.Invoke(_requestIdsToContracts.BidAsk[reqId].Symbol, bidAsk);
        }

        public Action<string, Last>? TickByTickAllLast;
        public void tickByTickAllLast(int reqId, int tickType, long time, double price, int size, TickAttribLast tickAttribLast, string exchange, string specialConditions)
        {
            var last = new Last()
            {
                Time = time,
                Price = price,
                Size = size,
                TickAttribLast = tickAttribLast,
                Exchange = exchange,
                SpecialConditions = specialConditions,
            };

            //_logger.Trace($"tickByTickAllLast ({reqId}) : {last}");
            TickByTickAllLast?.Invoke(_requestIdsToContracts.Last[reqId].Symbol, last);
        }

        public Action<int, IBApi.ContractDetails>? ContractDetails;
        public void contractDetails(int reqId, IBApi.ContractDetails contractDetails)
        {
            //_logger.Trace($"contractDetails ({reqId}) : {contractDetails.Contract.Symbol}");
            ContractDetails?.Invoke(reqId, contractDetails);
        }

        public Action<int>? ContractDetailsEnd;
        public void contractDetailsEnd(int reqId)
        {
            //_logger.Trace($"contractDetailsEnd ({reqId})");
            ContractDetailsEnd?.Invoke(reqId);
        }

        public Action<Contract, Order, OrderState>? OpenOrder;
        public void openOrder(int orderId, IBApi.Contract contract, IBApi.Order order, IBApi.OrderState orderState)
        {
            //_logger.Trace($"openOrder {orderId} : {c}, {o}, {os.Status}");
            // if (!string.IsNullOrEmpty(os.WarningText))
                //_logger.Warn($"openOrder {orderId} : Warning {os.WarningText}");

            OpenOrder?.Invoke(contract, order, orderState);
        }

        public Action? OpenOrderEnd;

        // This is not called when placing an order.
        public void openOrderEnd()
        {
            //_logger.Trace($"openOrderEnd");
            OpenOrderEnd?.Invoke();
        }

        public Action<OrderStatus>? OrderStatus;
        public void orderStatus(int orderId, string status, double filled, double remaining, double avgFillPrice, int permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice)
        {
            var os = new OrderStatus()
            {
                OrderId = orderId,
                Status = status,
                Filled = filled,
                Remaining = remaining,
                AvgFillPrice = avgFillPrice,
                PermId = permId,
                ParentId = parentId,
                LastFillPrice = lastFillPrice,
                ClientId = clientId,
                MktCapPrice = mktCapPrice,
            };

            //_logger.Trace($"orderStatus {orderId} : {os}");
            OrderStatus?.Invoke(os);
        }

        public Action<Contract, Execution>? ExecDetails;
        public void execDetails(int reqId, IBApi.Contract contract, Execution execution)
        {
            //_logger.Trace($"execDetails ({reqId}) : {ex}");
            ExecDetails?.Invoke(contract, execution);
        }

        public Action<int>? ExecDetailsEnd;
        public void execDetailsEnd(int reqId)
        {
            //_logger.Trace($"execDetailsEnd : reqId={reqId}");
            ExecDetailsEnd?.Invoke(reqId);
        }

        public Action<CommissionReport>? CommissionReport;
        public void commissionReport(CommissionReport commissionReport)
        {
            //_logger.Trace($"commissionReport : commission={commissionReport.Commission} Currency={commissionReport.Currency} RealizedPNL={commissionReport.RealizedPNL}");
            CommissionReport?.Invoke(commissionReport);
        }

        public Action<Contract, Order, OrderState>? CompletedOrder;
        public void completedOrder(IBApi.Contract contract, IBApi.Order order, IBApi.OrderState orderState)
        {
            //_logger.Trace($"completedOrder {o.Id} : {c}, {o}, {os.Status}");
            // if (!string.IsNullOrEmpty(os.WarningText))
                //_logger.Warn($"completedOrder {o.Id} : Warning {os.WarningText}");

            CompletedOrder?.Invoke(contract, order, orderState);
        }

        public Action? CompletedOrdersEnd;
        public void completedOrdersEnd()
        {
            //_logger.Trace($"completedOrdersEnd");
            CompletedOrdersEnd?.Invoke();
        }

        public Action<int, Bar>? HistoricalData;
        public void historicalData(int reqId, IBApi.Bar bar)
        {
            HistoricalData?.Invoke(reqId, bar);
            //_logger.Trace($"historicalData : {bar}");
        }

        public Action<int, string, string>? HistoricalDataEnd;
        public void historicalDataEnd(int reqId, string start, string end)
        {
            HistoricalDataEnd?.Invoke(reqId, start, end);
            //_logger.Trace($"historicalDataEnd");
        }

        public Action<int, IEnumerable<HistoricalTickBidAsk>, bool>? HistoricalTicksBidAsk;
        public void historicalTicksBidAsk(int reqId, HistoricalTickBidAsk[] ticks, bool done)
        {
            HistoricalTicksBidAsk?.Invoke(reqId, ticks, done);
            //_logger.Trace($"historicalTicksBidAsk");
        }

        public Action<int, IEnumerable<HistoricalTickLast>, bool>? HistoricalTicksLast;
        public void historicalTicksLast(int reqId, HistoricalTickLast[] ticks, bool done)
        {
            HistoricalTicksLast?.Invoke(reqId, ticks, done);
            //_logger.Trace($"historicalTicksLast");
        }

        public Action<long>? CurrentTime;
        public void currentTime(long time)
        {
            //_logger.Trace($"currentTime");
            CurrentTime?.Invoke(time);
        }

        public static bool IsWarningMessage(int code) => code >= 2100 && code < 2200;

        //// https://interactivebrokers.github.io/tws-api/message_codes.html
        //// https://interactivebrokers.github.io/tws-api/classIBApi_1_1EClientErrors.html
        //public static bool IsSystemMessage(int code) => code >= 1100 && code <= 1300;
        //public static bool IsWarningMessage(int code) => code >= 2100 && code < 2200;
        //public static bool IsClientErrorMessage(int code) => 
        //    (code >= 501 && code <= 508 && code != 507) ||
        //    (code >= 510 && code <= 549) ||
        //    (code >= 551 && code <= 584) ||
        //    code == 10038;

        //public static bool IsTWSErrorMessage(int code) =>
        //    (code >= 100 && code <= 168) ||
        //    (code >= 200 && code <= 449) ||
        //    code == 507 ||
        //    (code >= 10000 && code <= 10027) ||
        //    code == 10090 ||
        //    (code >= 10148 && code <= 10284);


        public Action<ErrorMessage>? Error;
        public void error(Exception e)
        {
            HandleError(new ErrorMessage(e));
        }

        public void error(string str)
        {
            HandleError(new ErrorMessage(str));
        }

        public void error(int id, int errorCode, string errorMsg)
        {
            HandleError(new ErrorMessage(id, errorCode, errorMsg));
        }

        void HandleError(ErrorMessage ex)
        {
            if (IsWarningMessage(ex.ErrorCode))
            {
                _logger?.Warn(ex);
                return;
            }

            // TODO : investigate which is better. Ideally it would be nice if I could just throw
            //throw ex;
            //_logger?.Error(ex);
            Error?.Invoke(ex);
        }

        #region Not Implemented

        public void accountUpdateMulti(int requestId, string account, string modelCode, string key, string value, string currency)
        {
            throw new NotImplementedException();
        }

        public void accountUpdateMultiEnd(int requestId)
        {
            throw new NotImplementedException();
        }

        public void bondContractDetails(int reqId, IBApi.ContractDetails contract)
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

        public void scannerData(int reqId, int rank, IBApi.ContractDetails contractDetails, string distance, string benchmark, string projection, string legsStr)
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

        #endregion Not Implemented
    }
}
