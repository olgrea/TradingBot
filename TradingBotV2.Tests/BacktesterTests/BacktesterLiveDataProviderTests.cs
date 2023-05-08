using NUnit.Framework;
using TradingBotV2.Backtesting;
using TradingBotV2.Tests;

namespace BacktesterTests
{
    internal class BacktesterLiveDataProviderTests : IBBrokerTests.LiveDataProviderTests
    {
        Backtester _backtester;

        [OneTimeSetUp]
        public override async Task OneTimeSetUp()
        {
            _backtester = TestsUtils.CreateBacktester(new DateOnly(2023, 04, 03));
            _broker = _backtester;
            await _broker.ConnectAsync();
        }

        [SetUp]
        public void SetUp()
        {
            _backtester.Reset();
            _ = _backtester.Start();
        }

        [TearDown]
        public void TearDown()
        {
            _backtester.Stop();
        }
    }
}
