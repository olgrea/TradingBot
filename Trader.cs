using System;
using System.Collections.Generic;
using System.Text;
using TradingBot.Broker;
using TradingBot.Strategies;

namespace TradingBot
{
    public class Trader
    {
        IBroker _broker;
        List<IStrategy> _strategies;

        public Trader()
        {

        }

        public void Start()
        {
            
        }

        void Trade()
        {
            try
            {
                while(true)
                {
                    foreach(IStrategy strategy in _strategies)
                    {
                        if(strategy.Evaluate(out Order order))
                        {
                            //_broker.PlaceOrder(order);
                        }
                    }
                }
            }
            catch(Exception e)
            {
                //_broker.SellEverything();
            }
        }

        public void Stop()
        {

        }
    }
}
