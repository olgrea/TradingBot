using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using IBApi;
using TradingBot.Utils;


namespace TradingBot.Broker.Client
{
    internal partial class TWSClient : EWrapper
    {
        const int DefaultPort = 7496;
        const string DefaultIP = "127.0.0.1";
        int _clientId = 1337;

        int _nextValidId = -1;

        EClientSocket _clientSocket;
        EReaderSignal _signal;
        EReader _reader;

        ILogger _logger;
        Task _processMsgTask;

        string _accountCode = null;
        public Action<Account> AccountReceived;
        Account _tmpAccount;

        public TWSClient(ILogger logger)
        {
            _signal = new EReaderMonitorSignal();
            _clientSocket = new EClientSocket(this, _signal);
            _logger = logger;
        }

        public void Connect()
        {
            _clientSocket.eConnect(DefaultIP, DefaultPort, _clientId);
            _reader = new EReader(_clientSocket, _signal);
            _reader.Start();
            _processMsgTask = Task.Run(ProcessMsg);
            _logger.LogDebug($"Reader started and is listening to messages from TWS");
        }

        public void Disconnect()
        {
            _clientSocket.reqAccountUpdates(false, _accountCode);
            _clientSocket.eConnect(DefaultIP, DefaultPort, _clientId);
        }

        public bool IsConnected => _clientSocket.IsConnected();

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
            _logger.LogDebug($"Connecting client {_clientId} to TWS on {DefaultIP}:{DefaultPort}...");
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

        public void nextValidId(int orderId)
        {
            if (_nextValidId < 0)
            {
                _logger.LogInfo($"Client {_clientId} connected.");
            }

            _nextValidId = orderId;
        }
        
        public void GetAccount()
        {
            _clientSocket.reqAccountUpdates(true, _accountCode);
            _tmpAccount = new Account() { Code = _accountCode };
        }

        public void updateAccountTime(string timestamp)
        {
            _logger.LogDebug($"Getting account time : {timestamp}");
            _tmpAccount.Time = DateTime.Parse(timestamp);
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
                    _tmpAccount.CashBalances.Add(new CashBalance(decimal.Parse(value, CultureInfo.InvariantCulture), currency));
                    break;

                case "RealizedPnL":
                    if (currency == "USD")
                        _tmpAccount.RealizedPnL = new CashBalance(decimal.Parse(value, CultureInfo.InvariantCulture), currency);
                    break;

                case "UnrealizedPnL":
                    if(currency == "USD")
                        _tmpAccount.UnrealizedPnL = new CashBalance(decimal.Parse(value, CultureInfo.InvariantCulture), currency);
                    break;
            }

        }
        
        public void updatePortfolio(IBApi.Contract contract, double position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName)
        {
            var pos = new Position()
            {
                Contract = ConvertContract(contract),
                PositionAmount = Convert.ToDecimal(position),
                MarketPrice = Convert.ToDecimal(marketPrice),
                MarketValue = Convert.ToDecimal(marketValue),
                AverageCost = Convert.ToDecimal(averageCost),
                UnrealizedPNL = Convert.ToDecimal(unrealizedPNL),
                RealizedPNL = Convert.ToDecimal(realizedPNL),
            };

            _logger.LogDebug($"Getting portfolio : \n{pos}");

            _tmpAccount.Positions.Add(pos);
        }

        Contract ConvertContract(IBApi.Contract ibc)
        {
            Contract contract = null;

            switch(ibc.SecType)
            {
                case "STK":
                    contract = new Stock()
                    {
                        Id = ibc.ConId,
                        Currency = ibc.Currency,
                        Exchange = ibc.Exchange,
                        Symbol = ibc.Symbol,
                        LastTradeDate = ibc.LastTradeDateOrContractMonth
                    };
                    break;

                case "OPT":
                    contract = new Option()
                    {
                        Id = ibc.ConId,
                        Currency = ibc.Currency,
                        Exchange = ibc.Exchange,
                        Symbol = ibc.Symbol,
                        ContractMonth = ibc.LastTradeDateOrContractMonth,
                        Strike = Convert.ToDecimal(ibc.Strike),
                        Multiplier = Decimal.Parse(ibc.Multiplier, CultureInfo.InvariantCulture),
                        OptionType = (ibc.Right == "C" || ibc.Right == "CALL") ? OptionType.Call : OptionType.Put,
                    };
                    break;

                default: 
                    throw new NotSupportedException($"This type of contract is not supported : {ibc.SecType}");
            }

            return contract;
        }

        public void accountDownloadEnd(string account)
        {
            AccountReceived?.Invoke(_tmpAccount);
        }

        public void error(Exception e)
        {
            _logger.LogError(e.Message);
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

        public void commissionReport(CommissionReport commissionReport)
        {
            throw new NotImplementedException();
        }

        public void completedOrder(IBApi.Contract contract, Order order, OrderState orderState)
        {
            throw new NotImplementedException();
        }

        public void completedOrdersEnd()
        {
            throw new NotImplementedException();
        }

        public void contractDetails(int reqId, ContractDetails contractDetails)
        {
            throw new NotImplementedException();
        }

        public void contractDetailsEnd(int reqId)
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

        public void execDetails(int reqId, IBApi.Contract contract, Execution execution)
        {
            throw new NotImplementedException();
        }

        public void execDetailsEnd(int reqId)
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

        public void historicalData(int reqId, Bar bar)
        {
            throw new NotImplementedException();
        }

        public void historicalDataEnd(int reqId, string start, string end)
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

        public void openOrder(int orderId, IBApi.Contract contract, Order order, OrderState orderState)
        {
            throw new NotImplementedException();
        }

        public void openOrderEnd()
        {
            throw new NotImplementedException();
        }

        public void orderBound(long orderId, int apiClientId, int apiOrderId)
        {
            throw new NotImplementedException();
        }

        public void orderStatus(int orderId, string status, double filled, double remaining, double avgFillPrice, int permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice)
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

        public void realtimeBar(int reqId, long date, double open, double high, double low, double close, long volume, double WAP, int count)
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

        public void tickByTickBidAsk(int reqId, long time, double bidPrice, double askPrice, int bidSize, int askSize, TickAttribBidAsk tickAttribBidAsk)
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
