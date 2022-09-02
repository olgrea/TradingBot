using System;
using System.Collections.Generic;
using System.Text;
using TradingBot.Broker;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.MarketData;
using TradingBot.Strategies;
using TradingBot.Utils;

namespace TradingBot
{
    public class Trader
    {


        ILogger _logger;
        IBroker _broker;
        List<IStrategy> _strategies = new List<IStrategy>();
        Dictionary<Contract, MarketData> _marketData = new Dictionary<Contract, MarketData>();
        Account _account;

        public Trader(int clientId, List<IStrategy> strategies, ILogger logger)
        {
            _logger = logger;
            _broker = new IBBroker(clientId, logger);
            _strategies = strategies;
        }

        public void Start()
        {
            _broker.Connect();
            _account = _broker.GetAccount();


            Contract contract = _broker.GetContract("GME");
            _broker.RequestBars(contract, BarLength._5Sec, OnBarsReceived);
            _broker.RequestBidAsk(contract, OnBidAskReceived);
        }

        void OnBidAskReceived(Contract contract, BidAsk bidAsk)
        {
            if (!_marketData.ContainsKey(contract))
                _marketData.Add(contract, new MarketData());

            _marketData[contract].BidAsk = bidAsk;
        }

        void OnBarsReceived(Contract contract, Bar bar)
        {
            if (!_marketData.ContainsKey(contract))
                _marketData.Add(contract, new MarketData());

            _marketData[contract].Bar = bar;
        }

        public void Stop()
        {
            // TODO : error handling? try/catch all? Separate process that monitors the main one?

            // Kill task
            //_broker.SellEverything();

            //_broker.CancelBarsRequest()
            //_broker.CancelBidAskRequest();
        }
    }
}
