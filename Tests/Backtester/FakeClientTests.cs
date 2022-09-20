using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using TradingBot.Broker.MarketData;
using TradingBot.Utils;
using System.IO;
using HistoricalDataFetcher;
using Backtester;
using TradingBot.Broker.Client;
using TradingBot.Broker;
using TradingBot.Broker.Orders;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection.Metadata.Ecma335;
using NUnit.Framework.Internal.Execution;

namespace Tests.Backtester
{
    [TestFixture]
    public class FakeClientTests
    {
        const string Ticker = "GME";

        DateTime _downwardFileTime = new DateTime(2022, 09, 19, 11, 30, 00, DateTimeKind.Local);
        DateTime _downwardStart = new DateTime(2022, 09, 19, 11, 17, 00);
        IEnumerable<BidAsk> _downwardBidAsks;
        IEnumerable<Bar> _downwardBars;

        DateTime _upwardFileTime = new DateTime(2022, 09, 19, 14, 30, 00, DateTimeKind.Local);
        DateTime _upwardStart = new DateTime(2022, 09, 19, 14, 21, 00);
        IEnumerable<BidAsk> _upwardBidAsks;
        IEnumerable<Bar> _upwardBars;

        FakeClient _fakeClient;
        IBCallbacks _callbacks;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _downwardBidAsks = Deserialize<BidAsk>(_downwardFileTime, _downwardStart);
            _downwardBars = Deserialize<Bar>(_downwardFileTime, _downwardStart);
            _upwardBidAsks = Deserialize<BidAsk>(_upwardFileTime, _upwardStart);
            _upwardBars = Deserialize<Bar>(_upwardFileTime, _upwardStart);

            var logger = new ConsoleLogger();
            _callbacks = new IBCallbacks(logger);
            var client = new IBClient(_callbacks, logger);
            _fakeClient = new FakeClient(client, logger);
            _fakeClient.Connect(IBBroker.DefaultIP, IBBroker.DefaultPort, 8008);
        }

        IEnumerable<T> Deserialize<T>(DateTime fileTime, DateTime start) where T : IMarketData, new()
        {
            var path = Path.Combine(DataFetcher.RootDir, MarketDataUtils.MakeDataPath<T>(Ticker, fileTime));
            return MarketDataUtils.DeserializeData<T>(path).SkipWhile(i => i.Time < start);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _fakeClient.Disconnect();
        }

        [SetUp]
        public void SetUp()
        {
            
        }

        [TearDown]
        public void TearDown()
        {
            _fakeClient.Stop();
            _fakeClient.Reset();
        }

        /*
         * TODO : tests to write
         Place order : order not opened if not enough cash

         */

        [Test]
        public void MarketOrder_Buy_GetsFilledAtCurrentAskPrice()
        {
            // Setup
            _fakeClient.Init(Ticker, _upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
            var order = new MarketOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.BUY,
                TotalQuantity = 50
            };

            // Test
            var expectedPrice = _upwardBidAsks.First().Ask;
            var actualPrice = AsyncToSync(() =>
            {
                _fakeClient.Start();
                _fakeClient.PlaceOrder(_fakeClient.Contract, order);

            }, ref _callbacks.ExecDetails, (c, oe) => { return oe.AvgPrice; });

            // Assert
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public void MarketOrder_Sell_GetsFilledAtCurrentBidPrice()
        {
            // Setup
            _fakeClient.Init(Ticker, _upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
            var order = new MarketOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.SELL,
                TotalQuantity = 50
            };

            var position = _fakeClient.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            var expectedPrice = _upwardBidAsks.First().Bid;
            var actualPrice = AsyncToSync(() =>
            {
                _fakeClient.Start();
                _fakeClient.PlaceOrder(_fakeClient.Contract, order);

            }, ref _callbacks.ExecDetails, (c, oe) => { return oe.AvgPrice; });

            // Assert
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        public TResult AsyncToSync<T1, TResult>(Action async, ref Action<T1> @event, Func<T1, TResult> resultFunc)
        {
            var tcs = new TaskCompletionSource<TResult>();
            var callback = new Action<T1>(t1 => tcs.SetResult(resultFunc(t1)));
            try
            {
                @event += callback;
                async.Invoke();
                tcs.Task.Wait(2000);
            }
            finally
            {
                @event -= callback;
            }

            return tcs.Task.IsCompletedSuccessfully ? tcs.Task.Result : default(TResult);
        }

        public TResult AsyncToSync<T1, T2, TResult>(Action async, ref Action<T1, T2> @event, Func<T1, T2, TResult> resultFunc)
        {
            var tcs = new TaskCompletionSource<TResult>();
            var callback = new Action<T1, T2>((t1, t2) => tcs.SetResult(resultFunc(t1, t2)));
            try
            {
                @event += callback;
                async.Invoke();
                tcs.Task.Wait(2000);
            }
            finally
            {
                @event -= callback;
            }

            return tcs.Task.IsCompletedSuccessfully ? tcs.Task.Result : default(TResult);
        }

        public TResult AsyncToSync<T1, T2, T3, TResult>(Action async, ref Action<T1, T2, T3> @event, Func<T1, T2, T3, TResult> resultFunc)
        {
            var tcs = new TaskCompletionSource<TResult>();
            var callback = new Action<T1, T2, T3>((t1, t2, t3) => tcs.SetResult(resultFunc(t1, t2, t3)));
            try
            {
                @event += callback;
                async.Invoke();
                tcs.Task.Wait(2000);
            }
            finally
            {
                @event -= callback;
            }

            return tcs.Task.IsCompletedSuccessfully ? tcs.Task.Result : default(TResult);
        }
    }
}
