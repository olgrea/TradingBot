using NLog.Targets;
using NUnit.Framework;

namespace NLog.NUnit
{
    [Target("NunitLogger")]
    public sealed class NunitTargetLogger : TargetWithLayout
    {
        protected override void Write(LogEventInfo logEvent)
        {
            string logMessage = Layout.Render(logEvent);
            TestContext.Progress.WriteLine(logMessage);
        }
    }
}
