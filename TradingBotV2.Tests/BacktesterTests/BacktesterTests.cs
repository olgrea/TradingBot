using NUnit.Framework;
using IBBrokerTests;
using TradingBotV2.Backtesting;

namespace BacktesterTests
{
    internal class BacktesterTests : IBBrokerTests.BrokerTests
    {
        [SetUp]
        public override async Task SetUp()
        {
            _broker = new Backtester(DateTime.Now);
            await Task.CompletedTask;
        }

        [TearDown]
        public override async Task TearDown()
        {
            await _broker.DisconnectAsync();
        }
    }
}
