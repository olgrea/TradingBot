using System.Collections.Generic;
using System.Threading.Tasks;

namespace TradingBot.Strategies
{
    public interface IOrderStrategy
    {
        Task ManageOrders(TradeSignal signal);
    }
}
