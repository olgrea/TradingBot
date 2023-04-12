using NUnit.Framework;
using IBBrokerTests;
using TradingBotV2.Backtesting;

namespace BacktesterTests
{
    internal class BacktesterBrokerTests : IBBrokerTests.BrokerTests
    {
        [SetUp]
        public override async Task SetUp()
        {
            _broker = new Backtester(DateTime.Now.AddDays(-1));
            await Task.CompletedTask;
        }
    }
}
