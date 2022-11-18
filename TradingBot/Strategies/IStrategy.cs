using System;

namespace TradingBot.Strategies
{
    public interface IStrategy
    {
        TimeSpan StartTime { get; }
        TimeSpan EndTime { get; }
        IIndicatorStrategy IndicatorStrategy { get; }
        IOrderStrategy OrderStrategy { get; }
    }
}
