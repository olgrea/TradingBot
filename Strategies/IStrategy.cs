using System.Collections.Generic;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;

namespace TradingBot.Strategies
{
    internal interface IStrategy
    {
        void Start();
        void Stop();

        public IReadOnlyDictionary<string, IState> States { get; }
        public IState CurrentState { get; }
    }
}
