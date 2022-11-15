namespace TradingBot.Strategies.Private.Indicators
{
    internal interface ITradeSignalState
    {

        TradeSignal GenerateTradeSignal();
    }
}
