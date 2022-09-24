using System.Threading.Tasks;
using TradingBot.Utils;

namespace TradingBot.Broker.Client
{
    internal interface IMessageHandler
    {
        IMessageHandler Successor { get;}
        void OnMessage(TWSMessage msg);
    }
}
