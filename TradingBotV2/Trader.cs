using System.Runtime.CompilerServices;
using NLog;
using TradingBotV2.Broker;

[assembly: InternalsVisibleTo("TradingBotV2.Tests")]
namespace TradingBotV2
{
    public class Trader
    {
        IBroker _broker;
        ILogger _logger;

        public Trader(IBroker broker, ILogger logger)
        {
            _broker = broker;
            _logger = logger;
        }
    }
}