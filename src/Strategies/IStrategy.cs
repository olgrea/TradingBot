using System.Collections.Generic;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;
using TradingBot.Indicators;

namespace TradingBot.Strategies
{
    public interface IStrategy
    {
        IEnumerable<IIndicator> Indicators { get; }
        void ComputeIndicators(IEnumerable<Bar> bars);
        void Evaluate();
    }
}
