using InteractiveBrokers.Contracts;

namespace TradingBot.Strategies
{
    public enum TradeSignal
    {
        StrongSell = -100,
        Sell = -10,
        CautiousSell = -1,
        Neutral = 0,
        CautiousBuy = 1,
        Buy = 10,
        StrongBuy = 100,
    }
}
