using Broker.Backtesting;
using NUnit.Framework;
using Broker.Tests;
using Broker.IBKR;
using Broker.MarketData;

namespace BacktesterTests
{
    internal class BacktesterLiveDataProviderTests : IBBrokerTests.LiveDataProviderTests
    {
        Backtester _backtester;

        // TODO : move that to separate project
        //[Test]
        public async Task FillTestDataDb()
        {
            string ticker = "SPY";

            var logger = TestsUtils.CreateLogger();
            var broker = TestsUtils.CreateBroker(logger);
            var hdp = (IBHistoricalDataProvider)broker.HistoricalDataProvider;

            var path = hdp.DbPath;
            try
            {
                hdp.DbPath = TestsUtils.TestDataDbPath;
                DateOnly date = new DateOnly(2024, 01, 10);
                await hdp.GetHistoricalDataAsync<Bar>(ticker, date);
                await hdp.GetHistoricalDataAsync<Last>(ticker, date);
                await hdp.GetHistoricalDataAsync<BidAsk>(ticker, date);
            }
            finally
            {
                hdp.DbPath = path;
                await broker.DisconnectAsync();
            }
        }

        [OneTimeSetUp]
        public override async Task OneTimeSetUp()
        {
            _backtester = TestsUtils.CreateBacktester(new DateOnly(2024, 01, 10));
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
