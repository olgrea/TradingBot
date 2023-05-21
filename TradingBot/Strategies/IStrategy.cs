using TradingBot.Indicators;

namespace TradingBot.Strategies
{
    public interface IStrategy
    {
        DateTime StartTime { get; }
        DateTime EndTime { get; }
        IEnumerable<IIndicator> Indicators { get; init; }
        Task Start();
        Task Initialize();
    }
}
