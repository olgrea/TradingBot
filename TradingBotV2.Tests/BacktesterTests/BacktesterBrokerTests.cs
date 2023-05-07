using NUnit.Framework;
using TradingBotV2.Backtesting;
using TradingBotV2.Tests;

namespace BacktesterTests
{
    internal class BacktesterBrokerTests : IBBrokerTests.BrokerTests
    {
        DateOnly _openDay;
        Backtester _backtester;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _openDay = TestsUtils.FindLastOpenDay();
        }

        [SetUp]
        public override async Task SetUp()
        {
            _backtester = TestsUtils.CreateBacktester(_openDay);
            _broker = _backtester;

            _accountCode = await _broker.ConnectAsync();
            var task = _backtester.Start();
            _ = task.ContinueWith(t => _tcs.TrySetException(t.Exception ?? new Exception("Unknown exception")), TaskContinuationOptions.OnlyOnFaulted);

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
