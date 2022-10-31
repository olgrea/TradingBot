using TradingBot.Broker;

namespace TradingBot.Indicators
{
    internal enum OrderSignalType
    {
        Overbought,
        Oversold,
    }

    public class IndicatorSignal
    {
        Contract Contract { get; set; } 
    }
}
