using System.Collections.Generic;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;

namespace TradingBot.Strategies
{
    public interface IStrategy
    {
        void Start();
        void Stop();

        public Dictionary<string, IState> States { get; }
    }
}
