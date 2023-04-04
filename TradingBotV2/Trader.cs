using System.Runtime.CompilerServices;
using TradingBotV2.Broker;

[assembly: InternalsVisibleTo("TradingBotV2.Tests")]
namespace TradingBotV2
{
    public class Trader
    {
        IBroker broker;
    }
}