using TradingBot.Broker;

namespace TradingBot.Strategies
{
    public interface IStrategy
    {
        void Start();
        void Stop();

        Trader Trader { get; }
    }
}
