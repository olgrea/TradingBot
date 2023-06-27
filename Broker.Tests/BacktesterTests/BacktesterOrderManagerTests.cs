using Broker.Backtesting;
using Broker.Utils;
using NUnit.Framework;
using Broker.Tests;

namespace BacktesterTests
{
    internal class BacktesterOrderManagerTests : IBBrokerTests.OrderManagerTests
    {
        Backtester _backtester;

        [OneTimeSetUp]
        public override async Task OneTimeSetUp()
        {
            // The 10:55:00 here is just so the order gets filled rapidly in test AwaitExecution_OrderGetsFilled_Returns ...
            DateTime from = new DateTime(2023, 04, 10, 10, 55, 00);
            DateTime to = new DateTime(2023, 04, 10).ToMarketHours().Item2;
            _backtester = TestsUtils.CreateBacktester(from, to);
            _backtester.TimeCompression.Factor = 0.002;
            _broker = _backtester;

            await Task.CompletedTask;
        }

        [SetUp]
        public override async Task SetUp()
        {
            await _broker.ConnectAsync();
            _backtester.Reset();
            _ = _backtester.Start();
        }
    }
}
