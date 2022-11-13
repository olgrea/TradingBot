namespace TradingBot.Strategies
{
    internal interface ITradeSignalState
    {

        TradeSignal GenerateTradeSignal();
    }
}
