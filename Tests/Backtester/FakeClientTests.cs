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

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _downwardBidAsks = Deserialize<BidAsk>(_downwardFileTime, _downwardStart);
            _downwardBars = Deserialize<Bar>(_downwardFileTime, _downwardStart);
            _upwardBidAsks = Deserialize<BidAsk>(_upwardFileTime, _upwardStart);
            _upwardBars = Deserialize<Bar>(_upwardFileTime, _upwardStart);

            var logger = new NoLogger();
            _fakeClient = new FakeClient(Ticker, logger);

            FakeClient.TimeDelays.TimeScale = 3600.0d / 162000;
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
            _fakeClient.Init(_upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
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

            }, ref _fakeClient.Callbacks.ExecDetails, (c, oe) => { return oe.AvgPrice; });

            // Assert
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public void MarketOrder_Sell_GetsFilledAtCurrentBidPrice()
        {
            // Setup
            _fakeClient.Init(_upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
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

            }, ref _fakeClient.Callbacks.ExecDetails, (c, oe) => { return oe.AvgPrice; });

            // Assert
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public void LimitOrder_Buy_OverAskPrice_GetsFilledAtCurrentAskPrice()
        {
            // Setup
            _fakeClient.Init(_upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
            var order = new LimitOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.BUY,
                LmtPrice = _upwardBidAsks.First().Ask + 0.5,
                TotalQuantity = 50
            };

            // Test
            var expectedPrice = _upwardBidAsks.First().Ask;
            var actualPrice = AsyncToSync(() =>
            {
                _fakeClient.Start();
                _fakeClient.PlaceOrder(_fakeClient.Contract, order);

            }, ref _fakeClient.Callbacks.ExecDetails, (c, oe) => { return oe.AvgPrice; });

            // Assert
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public void LimitOrder_Buy_UnderAskPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeClient.Init(_downwardStart, _downwardStart.AddMinutes(30), _downwardBars, _downwardBidAsks);
            var order = new LimitOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.BUY,
                LmtPrice = _downwardBidAsks.First().Ask - 0.02,
                TotalQuantity = 50
            };

            // Test
            var expectedPrice = _downwardBidAsks.First(ba => ba.Ask <= order.LmtPrice).Ask;
            var actualPrice = AsyncToSync(() =>
            {
                _fakeClient.Start();
                _fakeClient.PlaceOrder(_fakeClient.Contract, order);

            }, ref _fakeClient.Callbacks.ExecDetails, (c, oe) => { return oe.AvgPrice; }, 30);

            // Assert
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }


        [Test]
        public void LimitOrder_Sell_OverBidPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeClient.Init(_upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
            var order = new LimitOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.SELL,
                LmtPrice = _upwardBidAsks.First().Bid + 0.03,
                TotalQuantity = 50
            };

            var position = _fakeClient.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            var expectedPrice = _upwardBidAsks.First(ba => ba.Bid >= order.LmtPrice).Bid;
            var actualPrice = AsyncToSync(() =>
            {
                _fakeClient.Start();
                _fakeClient.PlaceOrder(_fakeClient.Contract, order);

            }, ref _fakeClient.Callbacks.ExecDetails, (c, oe) => { return oe.AvgPrice; }, 30);

            // Assert
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public void LimitOrder_Sell_UnderBidPrice_GetsFilledAtCurrentBidPrice()
        {
            // Setup
            _fakeClient.Init(_downwardStart, _downwardStart.AddMinutes(30), _downwardBars, _downwardBidAsks);
            var order = new LimitOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.SELL,
                LmtPrice = _downwardBidAsks.First().Bid - 0.1,
                TotalQuantity = 50
            };

            var position = _fakeClient.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            var expectedPrice = _downwardBidAsks.First().Bid;
            var actualPrice = AsyncToSync(() =>
            {
                _fakeClient.Start();
                _fakeClient.PlaceOrder(_fakeClient.Contract, order);

            }, ref _fakeClient.Callbacks.ExecDetails, (c, oe) => { return oe.AvgPrice; }, 30);

            // Assert
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public void StopOrder_Buy_OverAskPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeClient.Init(_upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
            var order = new StopOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.BUY,
                StopPrice = _upwardBidAsks.First().Ask + 0.02,
                TotalQuantity = 50
            };

            // Test
            var expectedPrice = _upwardBidAsks.First(ba => ba.Ask >= order.StopPrice).Ask;
            var actualPrice = AsyncToSync(() =>
            {
                _fakeClient.Start();
                _fakeClient.PlaceOrder(_fakeClient.Contract, order);

            }, ref _fakeClient.Callbacks.ExecDetails, (c, oe) => { return oe.AvgPrice; });

            // Assert
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public void StopOrder_Buy_UnderAskPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            _fakeClient.Init(_downwardStart, _downwardStart.AddMinutes(30), _downwardBars, _downwardBidAsks);
            var order = new StopOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.BUY,
                StopPrice = _downwardBidAsks.First().Ask - 0.1,
                TotalQuantity = 50
            };

            // Test
            var expectedPrice = _downwardBidAsks.First().Ask;
            var actualPrice = AsyncToSync(() =>
            {
                _fakeClient.Start();
                _fakeClient.PlaceOrder(_fakeClient.Contract, order);

            }, ref _fakeClient.Callbacks.ExecDetails, (c, oe) => { return oe.AvgPrice; }, 30);

            // Assert
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }


        [Test]
        public void StopOrder_Sell_OverBidPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            _fakeClient.Init(_upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
            var order = new StopOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.SELL,
                StopPrice = _upwardBidAsks.First().Bid + 0.03,
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

            }, ref _fakeClient.Callbacks.ExecDetails, (c, oe) => { return oe.AvgPrice; }, 30);

            // Assert
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public void StopOrder_Sell_UnderBidPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeClient.Init(_downwardStart, _downwardStart.AddMinutes(30), _downwardBars, _downwardBidAsks);
            var order = new StopOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.SELL,
                StopPrice = _downwardBidAsks.First().Bid - 0.02,
                TotalQuantity = 50
            };

            var position = _fakeClient.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            var expectedPrice = _downwardBidAsks.First(ba => ba.Bid <= order.StopPrice).Bid;
            var actualPrice = AsyncToSync(() =>
            {
                _fakeClient.Start();
                _fakeClient.PlaceOrder(_fakeClient.Contract, order);

            }, ref _fakeClient.Callbacks.ExecDetails, (c, oe) => { return oe.AvgPrice; }, 30);

            // Assert
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        TResult AsyncToSync<T1, T2, TResult>(Action async, ref Action<T1, T2> @event, Func<T1, T2, TResult> resultFunc, int timeoutInSec = 30)
        {
            var tcs = new TaskCompletionSource<TResult>();
            var callback = new Action<T1, T2>((t1, t2) => tcs.SetResult(resultFunc(t1, t2)));
            try
            {
                @event += callback;
                async.Invoke();
                tcs.Task.Wait(timeoutInSec*1000);
            }
            finally
            {
                @event -= callback;
            }

            return tcs.Task.IsCompletedSuccessfully ? tcs.Task.Result : default(TResult);
        }
    }
}
