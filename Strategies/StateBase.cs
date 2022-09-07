namespace TradingBot.Strategies
{
    public abstract class StateBase : IState
    {
        public IStrategy Strategy { get; private set; }
        public Trader Trader { get; private set; }

        public StateBase(IStrategy strategy, Trader trader)
        {
            Strategy = strategy;
            Trader = trader;
        }   

        public abstract IState Evaluate();

        public virtual void SubscribeToData()
        {
            
        }

        public virtual void UnsubscribeToData()
        {
            
        }
    }
}
