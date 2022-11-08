using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Backtester;
using DataStorage.Db.DbCommandFactories;
using InteractiveBrokers;
using InteractiveBrokers.MarketData;
using NUnit.Framework;
using Tests.Broker;

namespace Tests.Backtester
{
    [TestFixture]
    internal class BacktesterClientTests : IBClientTests
    {
        const string Symbol = "GME";

        DateTime _fileTime = new DateTime(2022, 10, 05, 09, 30, 00, DateTimeKind.Local);
        IEnumerable<BidAsk> _bidAsks;
        IEnumerable<Bar> _bars;
        BarCommandFactory _barCommandFactory;
        BidAskCommandFactory _bidAskCommandFactory;

        [OneTimeSetUp]
        public override async Task OneTimeSetUp()
        {
            _barCommandFactory = new BarCommandFactory(BarLength._1Sec);
            _bidAskCommandFactory = new BidAskCommandFactory();
            await Task.CompletedTask;
        }

        [SetUp]
        public override async Task SetUp()
        {
            _bars = _barCommandFactory.CreateSelectCommand(Symbol, _fileTime.Date).Execute();
            _bidAsks = _bidAskCommandFactory.CreateSelectCommand(Symbol, _fileTime.Date).Execute();

            var startTime = new DateTime(_fileTime.Date.Ticks + Utils.MarketStartTime.Ticks, DateTimeKind.Local);
            var endTime = new DateTime(_fileTime.Date.Ticks + Utils.MarketEndTime.Ticks, DateTimeKind.Local);

            var FakeIBSocket = new FakeIBSocket(Symbol, startTime, endTime, _bars, _bidAsks);
            _client = new IBClient(951, FakeIBSocket);

            _connectMessage = await _client.ConnectAsync();
            Assert.IsTrue(_connectMessage.AccountCode == "FAKEACCOUNT123");
        }
    }
}
