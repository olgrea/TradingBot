using TradingBot.Broker.Orders;

namespace TradingBot.Strategies
{
    public interface IState
    {
        IState Evaluate();
        void OrderUpdated(OrderStatus os, OrderExecution oe);
    }
}
