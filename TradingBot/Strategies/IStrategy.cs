using Skender.Stock.Indicators;

namespace TradingBot.Strategies
{
    public interface IStrategy
    {
        DateTime StartTime { get; }
        DateTime EndTime { get; }
        Task Start();
        Task Initialize();
    }
}
