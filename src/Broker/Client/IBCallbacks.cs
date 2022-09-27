using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IBApi;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Utils;

namespace TradingBot.Broker.Client
{
    internal class IBCallbacks : EWrapper
    {
        ILogger _logger;

        public IBCallbacks(ILogger logger)
        {
            _logger = logger;
            ErrorHandler = new DefaultErrorHandler(logger);
        }

        public Action ConnectAck;
        public void connectAck()
        {
            _logger.LogDebug($"Connecting client to TWS...");
            ConnectAck?.Invoke();
        }

        public Action ConnectionClosed;
        public void connectionClosed()
        {
            _logger.LogDebug($"Connection closed");
            ConnectionClosed?.Invoke();
        }

        public Action<string> ManagedAccounts;
        public void managedAccounts(string accountsList)
        {
            _logger.LogDebug($"Account list : {accountsList}");
            ManagedAccounts?.Invoke(accountsList);
        }

        public Action<int> NextValidId;
        public void nextValidId(int orderId)
        {
            NextValidId?.Invoke(orderId);
        }

        public Action<string> UpdateAccountTime;
        public void updateAccountTime(string timestamp)
        {
            _logger.LogDebug($"Getting account time : {timestamp}");
            UpdateAccountTime?.Invoke(timestamp);
        }

        public Action<string, string, string> UpdateAccountValue;
        public void updateAccountValue(string key, string value, string currency, string accountName)
        {
            _logger.LogDebug($"account value : {key} {value} {currency}");
            UpdateAccountValue?.Invoke(key, value, currency);

            //TODO : handle "AccountReady"
            //case "AccountReady":
            //    if (!bool.Parse(value))
            //    {
            //        string msg = "Account not available at the moment. The IB server is in the process of resetting. Values returned may not be accurate. Try again later";
            //        error(msg);
            //    }
            //    break;
        }

        public Action<Position> UpdatePortfolio;
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
            UpdatePortfolio?.Invoke(pos);
        }

        public Action<string> AccountDownloadEnd;
        public void accountDownloadEnd(string account)
        {
            AccountDownloadEnd?.Invoke(account);
        }

        public Action<Position> Position;
        public void position(string account, IBApi.Contract contract, double pos, double avgCost)
        {
            var p = new Position()
            {
                Contract = contract.ToTBContract(),
                PositionAmount = pos,
                AverageCost = avgCost,
            };

            _logger.LogDebug($"position received for account {account} : contract={p.Contract} pos={pos} avgCost={avgCost}");
            Position?.Invoke(p);
        }

        public Action PositionEnd;
        public void positionEnd()
        {
            _logger.LogDebug($"positionEnd");
            PositionEnd?.Invoke();
        }

        public Action<int, PnL> PnlSingle;
        public void pnlSingle(int reqId, int pos, double dailyPnL, double unrealizedPnL, double realizedPnL, double value)
        {
            var pnl = new PnL()
            {
                PositionAmount = pos,
                MarketValue = value,
                DailyPnL = dailyPnL,
                UnrealizedPnL = unrealizedPnL,
                RealizedPnL = realizedPnL
            };

            _logger.LogDebug($"PnL : {pnl}");
            PnlSingle?.Invoke(reqId, pnl);
        }

        // called at 5 sec intervals
        public Action<int, MarketData.Bar> RealtimeBar;
        public void realtimeBar(int reqId, long date, double open, double high, double low, double close, long volume, double WAP, int count)
        {
            RealtimeBar?.Invoke(reqId, new MarketData.Bar()
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

        public Action<int, BidAsk> TickByTickBidAsk;
        public void tickByTickBidAsk(int reqId, long time, double bidPrice, double askPrice, int bidSize, int askSize, TickAttribBidAsk tickAttribBidAsk)
        {
            var bidAsk = new BidAsk()
            {
                Bid = bidPrice,
                BidSize = bidSize,
                Ask = askPrice,
                AskSize = askSize,
                Time = DateTimeOffset.FromUnixTimeSeconds(time).DateTime.ToLocalTime(),
            };

            TickByTickBidAsk?.Invoke(reqId, bidAsk);
        }

        public Action<int, Contract> ContractDetails;
        public void contractDetails(int reqId, ContractDetails contractDetails)
        {
            ContractDetails?.Invoke(reqId, contractDetails.Contract.ToTBContract());
        }

        public Action<int> ContractDetailsEnd;
        public void contractDetailsEnd(int reqId)
        {
            ContractDetailsEnd?.Invoke(reqId);
        }

        public Action<Contract, Orders.Order, Orders.OrderState> OpenOrder;
        public void openOrder(int orderId, IBApi.Contract contract, IBApi.Order order, IBApi.OrderState orderState)
        {
            // orderState only used then IBApi.Order.WhatIf = true ?? 
            _logger.LogDebug($"openOrder {orderId} : {orderState.Status}");
            OpenOrder?.Invoke(contract.ToTBContract(), order.ToTBOrder(), orderState.ToTBOrderState());
        }

        public Action OpenOrderEnd;
        public void openOrderEnd()
        {
            _logger.LogDebug($"openOrderEnd");
            OpenOrderEnd?.Invoke();
        }

        public Action<OrderStatus> OrderStatus;
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

            OrderStatus?.Invoke(os);
        }

        public Action<Contract, OrderExecution> ExecDetails;
        public void execDetails(int reqId, IBApi.Contract contract, Execution execution)
        {
            _logger.LogDebug($"execDetails : reqId={reqId}");
            ExecDetails?.Invoke(contract.ToTBContract(), execution.ToTBExecution());
        }

        public Action<int> ExecDetailsEnd;
        public void execDetailsEnd(int reqId)
        {
            _logger.LogDebug($"execDetailsEnd : reqId={reqId}");
            ExecDetailsEnd?.Invoke(reqId);
        }

        public Action<CommissionInfo> CommissionReport;
        public void commissionReport(CommissionReport commissionReport)
        {
            _logger.LogDebug($"commissionReport : commission={commissionReport.Commission} Currency={commissionReport.Currency} RealizedPNL={commissionReport.RealizedPNL}");
            CommissionReport?.Invoke(commissionReport.ToTBCommission());
        }

        public Action<Contract, Orders.Order, Orders.OrderState> CompletedOrder;
        public void completedOrder(IBApi.Contract contract, IBApi.Order order, IBApi.OrderState orderState)
        {
            _logger.LogDebug($"completedOrder {order.OrderId} : {orderState.Status}");
            CompletedOrder?.Invoke(contract.ToTBContract(), order.ToTBOrder(), orderState.ToTBOrderState());
        }

        public Action CompletedOrdersEnd;
        public void completedOrdersEnd()
        {
            _logger.LogDebug($"completedOrdersEnd");
            CompletedOrdersEnd?.Invoke();
        }

        public Action<int, MarketData.Bar> HistoricalData;
        public void historicalData(int reqId, IBApi.Bar bar)
        {
            var b = new MarketData.Bar()
            {
                Open = bar.Open,
                Close = bar.Close,
                High = bar.High,
                Low = bar.Low,
                Volume = bar.Volume,
                TradeAmount = bar.Count,

                // non-standard date format...
                Time = DateTime.SpecifyKind(DateTime.ParseExact(bar.Time, "yyyyMMdd  HH:mm:ss", CultureInfo.InvariantCulture), DateTimeKind.Local)
            };
            HistoricalData?.Invoke(reqId, b);
            _logger.LogDebug($"historicalData for : {bar.Time}");
        }

        public Action<int, string, string> HistoricalDataEnd;
        public void historicalDataEnd(int reqId, string start, string end)
        {
            HistoricalDataEnd?.Invoke(reqId, start, end);
            _logger.LogDebug($"historicalDataEnd");
        }

        public Action<int, IEnumerable<BidAsk>, bool> HistoricalTicksBidAsk;
        public void historicalTicksBidAsk(int reqId, HistoricalTickBidAsk[] ticks, bool done)
        {
            IEnumerable<BidAsk> bas = ticks.Select(t => new BidAsk() 
            {
                Bid = t.PriceBid,
                BidSize = Convert.ToInt32(t.SizeBid),
                Ask = t.PriceAsk,
                AskSize = Convert.ToInt32(t.SizeAsk),
                Time = DateTimeOffset.FromUnixTimeSeconds(t.Time).DateTime.ToLocalTime(),
            });

            HistoricalTicksBidAsk?.Invoke(reqId, bas, done);
            _logger.LogDebug($"historicalTicksBidAsk");
        }

        public IErrorHandler ErrorHandler;
        public void error(Exception e)
        {
            ErrorHandler?.OnError(new APIError(e));
        }

        public void error(string str)
        {
            ErrorHandler?.OnError(new ErrorMessage(str));
        }

        public void error(int id, int errorCode, string errorMsg)
        {
            ErrorHandler?.OnError(new ErrorMessage(id, errorCode, errorMsg));
        }

        #region Not Implemented

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

        #endregion Not Implemented
    }
}
