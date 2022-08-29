using TradingBot.Broker;

namespace TradingBot.Strategies
{
    public interface IStrategy
    {
        void Start();
        void Stop();

        Contract Contract { get; }
        IBroker Broker { get; }
    }
}
