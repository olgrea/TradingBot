using System.Collections.Generic;
using InteractiveBrokers.MarketData;
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
