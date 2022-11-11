namespace TradingBot.Strategies
{
    public interface IState
    {
        IState Evaluate();
    }
}
