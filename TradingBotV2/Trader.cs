using System.Runtime.CompilerServices;
using TradingBotV2.Broker;

[assembly: InternalsVisibleTo("TradingBotV2.Tests")]
namespace TradingBotV2
{
    // TODO : investigate that new "non-nullable" project setting

    public class Trader
    {
        IBroker broker;
    }
}