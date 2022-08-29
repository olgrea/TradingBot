using System;
using System.Collections.Generic;
using System.Text;
using TradingBot.Broker;
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
        Dictionary<Contract, MarketData> MarketData = new Dictionary<Contract, MarketData>();


        public Trader(ILogger logger)
        {
            _logger = logger;
            _broker = new IBBroker(logger);
            _broker.Connect();
        }

        public void Start()
        {
            Contract contract = _broker.GetContract("GME");
            _broker.RequestBars(contract, BarLength.FiveSec, OnBarsReceived);
            _broker.RequestBidAsk(contract, OnBidAskReceived);
        }

        void OnBidAskReceived(Contract contract, BidAsk bidAsk)
        {
            if (!MarketData.ContainsKey(contract))
                MarketData.Add(contract, new MarketData());

            MarketData[contract].BidAsk= bidAsk;
        }

        void OnBarsReceived(Contract contract, Bar bar)
        {
            if (!MarketData.ContainsKey(contract))
                MarketData.Add(contract, new MarketData());

            MarketData[contract].Bar = bar;
        }

        void Trade()
        {
            try
            {
                while(true)
                {
                    foreach(IStrategy strategy in _strategies)
                    {
                        var data = MarketData[strategy.Contract];
                        if(strategy.Evaluate(data.Bar, data.BidAsk, out Order order))
                        {
                            //_broker.PlaceOrder(order);
                        }
                    }
                }
            }
            catch(Exception e)
            {
                //_broker.SellEverything();
                _logger.LogError(e.Message);
            }
        }

        public void Stop()
        {
            // Kill task
            //_broker.SellEverything();

            //_broker.CancelBarsRequest()
            //_broker.CancelBidAskRequest();
        }
    }
}
