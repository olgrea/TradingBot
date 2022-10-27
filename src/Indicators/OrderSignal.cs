using System;
using System.Collections.Generic;
using System.Text;
using IBApi;
using TradingBot.Broker.Orders;

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
