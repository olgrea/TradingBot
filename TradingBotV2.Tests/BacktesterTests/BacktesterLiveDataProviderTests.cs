using NUnit.Framework;
using TradingBotV2.Backtesting;
using TradingBotV2.Tests;

namespace BacktesterTests
{
    internal class BacktesterLiveDataProviderTests : IBBrokerTests.LiveDataProviderTests
    {
        Backtester _backtester;
        Task _backtestingTask;

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
            // TODO : Is there a better way?
            _backtester.Reset();
            _backtestingTask = _backtester.Start();
            _ = _backtestingTask.ContinueWith(t =>
            {
                var e = t.Exception ?? new Exception("Backtesting task faulted.");
                _tcs?.TrySetException(e);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        [TearDown]
        public void TearDown()
        {
            _backtester.Stop();
        }
    }
}
