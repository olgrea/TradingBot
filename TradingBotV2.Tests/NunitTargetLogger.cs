using NLog;
using NLog.Targets;
using NUnit.Framework;

namespace TradingBotV2.Tests
{
    public sealed class NunitTargetLogger : TargetWithLayout
    {
        protected override void Write(LogEventInfo logEvent)
        {
            string logMessage = this.Layout.Render(logEvent);
            TestContext.Progress.WriteLine(logMessage);
        }
    }
}
