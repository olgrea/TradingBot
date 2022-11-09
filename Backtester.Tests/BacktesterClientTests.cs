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
            var marketData = new MarketDataCollections()
            {
                Bars = _barCommandFactory.CreateSelectCommand(Symbol, _fileTime.Date).Execute(),
                BidAsks = _bidAskCommandFactory.CreateSelectCommand(Symbol, _fileTime.Date).Execute(),
            };

            var startTime = new DateTime(_fileTime.Date.Ticks + Utils.MarketStartTime.Ticks, DateTimeKind.Local);
            var endTime = new DateTime(_fileTime.Date.Ticks + Utils.MarketEndTime.Ticks, DateTimeKind.Local);

            var fakeSocket = new FakeClientSocket(Symbol, startTime, endTime, marketData);
            _client = new BacktesterClient(951, fakeSocket);

            _connectMessage = await _client.ConnectAsync();
            Assert.IsTrue(_connectMessage.AccountCode == "FAKEACCOUNT123");
        }
    }
}
