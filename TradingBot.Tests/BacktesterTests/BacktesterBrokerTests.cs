using NUnit.Framework;
using TradingBot.Backtesting;
using TradingBot.Tests;
using TradingBot.Utils;

namespace BacktesterTests
{
    internal class BacktesterBrokerTests : IBBrokerTests.BrokerTests
    {
        DateOnly _openDay;
        Backtester _backtester;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _openDay = MarketDataUtils.FindLastOpenDay(DateTime.Now.AddDays(-1));
        }

        [SetUp]
        public override async Task SetUp()
        {
            _backtester = TestsUtils.CreateBacktester(_openDay);
            _broker = _backtester;

            _accountCode = await _broker.ConnectAsync();
            _ = _backtester.Start();

            await Task.CompletedTask;
        }

        [TearDown]
        public override async Task TearDown()
        {
            _backtester.Stop();
            await _backtester.DisconnectAsync();
            await _backtester.DisposeAsync();
        }
    }
}
