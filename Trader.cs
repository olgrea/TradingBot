using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        HashSet<IStrategy> _strategies = new HashSet<IStrategy>();

        string _ticker;
        HashSet<Type> _desiredStrategies = new HashSet<Type>();

        Dictionary<Contract, MarketData> _marketData = new Dictionary<Contract, MarketData>();

        public Trader(string ticker, int clientId, ILogger logger)
        {
            Trace.Assert(!string.IsNullOrEmpty(ticker));

            _ticker = ticker;
            _logger = logger;
            _broker = new IBBroker(clientId, logger);
        }

        public IBroker Broker => _broker;

        public void AddStrategyForTicker<TStrategy>(string ticker) where TStrategy : IStrategy, new()
        {
            _desiredStrategies.Add(typeof(TStrategy));
        }

        public void Start()
        {
            _broker.Connect();
            
            if (!_desiredStrategies.Any())
                _logger.LogError("No strategies set for this trader");

            InitStrategies();

            //_account = _broker.GetAccount();
            // TODO : get available funds, registration to PnL by contract

            foreach(var strat in _strategies)
                strat.Start();

            //_broker.RequestBars(contract, BarLength._5Sec, OnBarsReceived);
            //_broker.RequestBidAsk(contract, OnBidAskReceived);
        }

        void InitStrategies()
        {
            Contract contract = _broker.GetContract(_ticker);
            // TODO : error if contract not received...

            foreach(var type in _desiredStrategies)
            {
                _strategies.Add((IStrategy)Activator.CreateInstance(type, contract, this));
            }
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
