using Skender.Stock.Indicators;

namespace TradingBotV2.Strategies
{
    public interface IStrategy
    {
        DateTime StartTime { get; }
        DateTime EndTime { get; }
        Task Start();
        Task Initialize();
    }
}
