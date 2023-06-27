using Broker.Indicators;

namespace Broker.Strategies
{
    public interface IStrategy
    {
        DateTime StartTime { get; }
        DateTime EndTime { get; }
        IEnumerable<IIndicator> Indicators { get; init; }
        Task Start(CancellationToken token);
        Task Initialize(CancellationToken token);
    }
}
