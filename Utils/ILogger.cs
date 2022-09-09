using System;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Utils
{
    internal interface ILogger
    {
        void LogDebug(string message);
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message);
    }
}
