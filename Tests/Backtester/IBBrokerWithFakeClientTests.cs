using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Tests.Broker;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;
using Backtester;
using System.IO;
using TradingBot.Utils;

namespace Tests.Backtester
{
    [TestFixture]
    internal class IBBrokerWithFakeClientTests : IBBrokerTests
    {
        const string Symbol = "GME";

        DateTime _fileTime = new DateTime(2022, 10, 05, 09, 30, 00, DateTimeKind.Local);
        IEnumerable<BidAsk> _bidAsks;
        IEnumerable<Bar> _bars;

        [OneTimeSetUp]
        public override async Task OneTimeSetUp()
        {
            await Task.CompletedTask;
        }

        [SetUp]
        public override async Task SetUp()
        {
            _bars = DbUtils.SelectData<Bar>(Symbol, _fileTime.Date);
            _bidAsks = DbUtils.SelectData<BidAsk>(Symbol, _fileTime.Date);

            var startTime = new DateTime(_fileTime.Date.Ticks + MarketDataUtils.MarketStartTime.Ticks, DateTimeKind.Local);
            var endTime = new DateTime(_fileTime.Date.Ticks + MarketDataUtils.MarketEndTime.Ticks, DateTimeKind.Local);

            var fakeClient = new FakeClient(Symbol, startTime, endTime, _bars, _bidAsks);
            _broker = new IBBroker(951, fakeClient);

            _connectMessage = await _broker.ConnectAsync();
            Assert.IsTrue(_connectMessage.AccountCode == "FAKEACCOUNT123");
        }
    }
}
