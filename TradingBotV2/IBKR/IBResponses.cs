using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using IBApi;

namespace TradingBotV2.IBKR
{
    public class IBResponses : EWrapper
    {
        public Action ConnectAck;
        public void connectAck()
        {
            ConnectAck?.Invoke();
        }

        public Action ConnectionClosed;
        public void connectionClosed()
        {
            ConnectionClosed?.Invoke();
        }

        public Action<IEnumerable<string>> ManagedAccounts;
        public void managedAccounts(string accountsList)
        {
            var accounts = accountsList.Split(',');
            ManagedAccounts?.Invoke(accounts);
        }

        public Action<int> NextValidId;
        public void nextValidId(int orderId)
        {
            NextValidId?.Invoke(orderId);
        }

        public Action<TimeSpan> UpdateAccountTime;
        public void updateAccountTime(string timestamp)
        {
            UpdateAccountTime?.Invoke(TimeSpan.Parse(timestamp, CultureInfo.InvariantCulture));
        }

        public Action<string, string, string, string> UpdateAccountValue;
        public void updateAccountValue(string key, string value, string currency, string accountName)
        {
            UpdateAccountValue?.Invoke(key, value, currency, accountName);
        }

        public Action<Contract, double, double, double, double, double, double, string> UpdatePortfolio;
        public void updatePortfolio(Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName)
        {
            UpdatePortfolio?.Invoke(contract, position, marketPrice, marketValue, averageCost, unrealizedPNL, realizedPNL, accountName);
        }

        public Action<string> AccountDownloadEnd;
        public void accountDownloadEnd(string account)
        {
            AccountDownloadEnd?.Invoke(account);
        }

        public Action<int, string, string, string, string> AccountSummary;
        public void accountSummary(int reqId, string account, string tag, string value, string currency)
        {
            AccountSummary?.Invoke(reqId, account, tag, value, currency);
        }

        public Action<int> AccountSummaryEnd;
        public void accountSummaryEnd(int reqId)
        {
            AccountSummaryEnd?.Invoke(reqId);
        }

        public Action<string, Contract, double, double> Position;
        public void position(string account, Contract contract, double pos, double avgCost)
        {
            Position?.Invoke(account, contract, pos, avgCost);
        }

        public Action PositionEnd;
        public void positionEnd()
        {
            PositionEnd?.Invoke();
        }

        public Action<int, int, double, double, double, double> PnlSingle;
        public void pnlSingle(int reqId, int pos, double dailyPnL, double unrealizedPnL, double realizedPnL, double value)
        {
            PnlSingle?.Invoke(reqId, pos, dailyPnL, unrealizedPnL, realizedPnL, value);
        }

        // called at 5 sec intervals
        public Action<int, long, double, double, double, double, long, double, int> RealtimeBar;
        public void realtimeBar(int reqId, long date, double open, double high, double low, double close, long volume, double WAP, int count)
        {
            RealtimeBar?.Invoke(reqId, date, open, high, low, close, volume, WAP, count);
        }

        public Action<int, long, double, double, int, int, TickAttribBidAsk> TickByTickBidAsk;
        public void tickByTickBidAsk(int reqId, long time, double bidPrice, double askPrice, int bidSize, int askSize, TickAttribBidAsk tickAttribBidAsk)
        {
            TickByTickBidAsk?.Invoke(reqId, time, bidPrice, askPrice, bidSize, askSize, tickAttribBidAsk);
        }

        // TODO : support 1 sec bars using tick by tick data?
        public Action<int, int, long, double, int, TickAttribLast, string, string> TickByTickAllLast;
        public void tickByTickAllLast(int reqId, int tickType, long time, double price, int size, TickAttribLast tickAttribLast, string exchange, string specialConditions)
        {
            TickByTickAllLast?.Invoke(reqId, tickType, time, price, size, tickAttribLast, exchange, specialConditions);
        }

        public Action<int, ContractDetails> ContractDetails;
        public void contractDetails(int reqId, ContractDetails contractDetails)
        {
            ContractDetails?.Invoke(reqId, contractDetails);
        }

        public Action<int> ContractDetailsEnd;
        public void contractDetailsEnd(int reqId)
        {
            ContractDetailsEnd?.Invoke(reqId);
        }

        public Action<int, Contract, Order, OrderState> OpenOrder;
        public void openOrder(int orderId, Contract contract, Order order, OrderState orderState)
        {
            // TODO : handle commented stuff
            //if (!string.IsNullOrEmpty(os.WarningText))
            //    _logger.Warn($"openOrder {orderId} : Warning {os.WarningText}");

            OpenOrder?.Invoke(orderId, contract, order, orderState);
        }

        public Action OpenOrderEnd;

        // This is not called when placing an order.
        public void openOrderEnd()
        {
            OpenOrderEnd?.Invoke();
        }

        public Action<int, string, double, double, double, int, int, double, int, string, double> OrderStatus;
        public void orderStatus(int orderId, string status, double filled, double remaining, double avgFillPrice, int permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice)
        {
            //var os = new OrderStatus()
            //{
            //    Info = new RequestInfo()
            //    {
            //        OrderId = orderId,
            //        ParentId = parentId,
            //        ClientId = clientId,
            //        PermId = permId,
            //    },
            //    Status = !string.IsNullOrEmpty(status) ? (Status)Enum.Parse(typeof(Status), status) : Status.Unknown,
            //    Filled = filled,
            //    Remaining = remaining,
            //    AvgFillPrice = avgFillPrice,
            //    LastFillPrice = lastFillPrice,
            //    MktCapPrice = mktCapPrice,
            //};

            OrderStatus?.Invoke(orderId, status, filled, remaining, avgFillPrice, permId, parentId, lastFillPrice, clientId, whyHeld, mktCapPrice);
        }

        public Action<int, Contract, Execution> ExecDetails;
        public void execDetails(int reqId, Contract contract, Execution execution)
        {
            ExecDetails?.Invoke(reqId, contract, execution);
        }

        public Action<int> ExecDetailsEnd;
        public void execDetailsEnd(int reqId)
        {
            ExecDetailsEnd?.Invoke(reqId);
        }

        public Action<CommissionReport> CommissionReport;
        public void commissionReport(CommissionReport commissionReport)
        {
            CommissionReport?.Invoke(commissionReport);
        }

        public Action<Contract, Order, OrderState> CompletedOrder;
        public void completedOrder(Contract contract, Order order, OrderState orderState)
        {
            //if (!string.IsNullOrEmpty(os.WarningText))
            //    _logger.Warn($"completedOrder {o.Id} : Warning {os.WarningText}");

            CompletedOrder?.Invoke(contract, order, orderState);
        }

        public Action CompletedOrdersEnd;
        public void completedOrdersEnd()
        {
            CompletedOrdersEnd?.Invoke();
        }

        public Action<int, Bar> HistoricalData;
        public void historicalData(int reqId, Bar bar)
        {
            //var b = new MarketData.Bar()
            //{
            //    Open = bar.Open,
            //    Close = bar.Close,
            //    High = bar.High,
            //    Low = bar.Low,
            //    Volume = bar.Volume,
            //    TradeAmount = bar.Count,

            //    // non-standard date format...
            //    Time = DateTime.SpecifyKind(DateTime.ParseExact(bar.Time, MarketDataUtils.TWSTimeFormat, CultureInfo.InvariantCulture), DateTimeKind.Local)
            //};
            HistoricalData?.Invoke(reqId, bar);
        }

        public Action<int, string, string> HistoricalDataEnd;
        public void historicalDataEnd(int reqId, string start, string end)
        {
            HistoricalDataEnd?.Invoke(reqId, start, end);
        }

        public Action<int, HistoricalTickBidAsk[], bool> HistoricalTicksBidAsk;
        public void historicalTicksBidAsk(int reqId, HistoricalTickBidAsk[] ticks, bool done)
        {
            HistoricalTicksBidAsk?.Invoke(reqId, ticks, done);
        }

        public Action<int, HistoricalTickLast[], bool> HistoricalTicksLast;
        public void historicalTicksLast(int reqId, HistoricalTickLast[] ticks, bool done)
        {
            //IEnumerable<Last> lasts = ticks.Select(t => new Last()
            //{
            //    Price = t.Price,
            //    Size = Convert.ToInt32(t.Size),
            //    Time = DateTimeOffset.FromUnixTimeSeconds(t.Time).DateTime.ToLocalTime(),
            //});

            HistoricalTicksLast?.Invoke(reqId, ticks, done);
        }

        public Action<DateTime> CurrentTime;
        public void currentTime(long time)
        {
            var datetime = DateTimeOffset.FromUnixTimeSeconds(time).DateTime.ToLocalTime();
            CurrentTime?.Invoke(datetime);
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


        public Action<ErrorMessage> Error;
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
                return;
            }

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

        public void bondContractDetails(int reqId, ContractDetails contract)
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

        public void historicalDataUpdate(int reqId, Bar bar)
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

        public void positionMulti(int requestId, string account, string modelCode, Contract contract, double pos, double avgCost)
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
