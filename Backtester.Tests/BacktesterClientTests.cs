using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Backtester;
using DataStorage.Db.DbCommandFactories;
using InteractiveBrokers;
using InteractiveBrokers.MarketData;
using NUnit.Framework;
using IBClient.Tests;

namespace Backtester.Tests
{
    [TestFixture]
    internal class BacktesterClientTests : IBClientTests
    {
        const string Symbol = "GME";

        DateTime _fileTime = new DateTime(2022, 10, 05, 09, 30, 00, DateTimeKind.Local);
        BarCommandFactory _barCommandFactory;
        BidAskCommandFactory _bidAskCommandFactory;
        LastCommandFactory _lastCommandFactory;

        [OneTimeSetUp]
        public override async Task OneTimeSetUp()
        {
            _barCommandFactory = new BarCommandFactory(BarLength._1Sec);
            _bidAskCommandFactory = new BidAskCommandFactory();
            _lastCommandFactory = new LastCommandFactory();
            await Task.CompletedTask;
        }

        [SetUp]
        public override async Task SetUp()
        {
            var marketData = new MarketDataCollections()
            {
                Bars = _barCommandFactory.CreateSelectCommand(Symbol, _fileTime.Date).Execute(),
                BidAsks = _bidAskCommandFactory.CreateSelectCommand(Symbol, _fileTime.Date).Execute(),
                Lasts = _lastCommandFactory.CreateSelectCommand(Symbol, _fileTime.Date).Execute(),
            };

            var startTime = new DateTime(_fileTime.Date.Ticks + MarketDataUtils.MarketStartTime.Ticks, DateTimeKind.Local);
            var endTime = new DateTime(_fileTime.Date.Ticks + MarketDataUtils.MarketEndTime.Ticks, DateTimeKind.Local);

            var fakeSocket = new BacktesterClientSocket(Symbol, startTime, endTime, marketData);
            _client = new BacktesterClient(951, fakeSocket);

            _connectMessage = await _client.ConnectAsync();
            Assert.IsTrue(_connectMessage.AccountCode == "FAKEACCOUNT123");
        }
    }
}
