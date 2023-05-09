using Skender.Stock.Indicators;

namespace TradingBotV2.Strategies
{
    public interface IStrategy
    {
        TimeSpan StartTime { get; }
        TimeSpan EndTime { get; }
        Task Start();
        Task Initialize();
    }
}
