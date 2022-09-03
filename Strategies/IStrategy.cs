using TradingBot.Broker;

namespace TradingBot.Strategies
{
    public interface IStrategy
    {
        void Start();
        void Stop();

        Contract Contract { get; }
        Trader Trader { get; }
    }
}
