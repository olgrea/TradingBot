using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backtester;
using HistoricalDataFetcher;
using NUnit.Framework;
using TradingBot.Broker;
using TradingBot.Broker.Client.Messages;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Utils;
[assembly: LevelOfParallelism(3)]

namespace Tests.Backtester
{
    [TestFixture]
    public class FakeClientTests
    {
        const string Symbol = "GME";

        DateTime _downwardFileTime = new DateTime(2022, 09, 19, 11, 30, 00, DateTimeKind.Local);
        DateTime _downwardStart = new DateTime(2022, 09, 19, 11, 17, 00);
        IEnumerable<BidAsk> _downwardBidAsks;
        IEnumerable<Bar> _downwardBars;

        DateTime _upwardFileTime = new DateTime(2022, 09, 19, 14, 30, 00, DateTimeKind.Local);
        DateTime _upwardStart = new DateTime(2022, 09, 19, 14, 21, 00);
        IEnumerable<BidAsk> _upwardBidAsks;
        IEnumerable<Bar> _upwardBars;

        DateTime _downThenUpFileTime = new DateTime(2022, 09, 19, 15, 30, 00, DateTimeKind.Local);
        DateTime _downThenUpStart = new DateTime(2022, 09, 19, 15, 15, 00);
        IEnumerable<BidAsk> _downThenUpBidAsks;
        IEnumerable<Bar> _downThenUpBars;

        DateTime _upThenDownFileTime = new DateTime(2022, 09, 19, 11, 30, 00, DateTimeKind.Local);
        DateTime _upThenDownStart = new DateTime(2022, 09, 19, 11, 13, 00);
        IEnumerable<BidAsk> _upThenDownBidAsks;
        IEnumerable<Bar> _upThenDownBars;

        FakeClient _fakeClient;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            _downwardBidAsks = Deserialize<BidAsk>(_downwardFileTime, _downwardStart);
            _downwardBars = Deserialize<Bar>(_downwardFileTime, _downwardStart);
            _upwardBidAsks = Deserialize<BidAsk>(_upwardFileTime, _upwardStart);
            _upwardBars = Deserialize<Bar>(_upwardFileTime, _upwardStart);
            _downThenUpBidAsks = Deserialize<BidAsk>(_downThenUpFileTime, _downThenUpStart);
            _downThenUpBars = Deserialize<Bar>(_downThenUpFileTime, _downThenUpStart);
            _upThenDownBidAsks = Deserialize<BidAsk>(_upThenDownFileTime, _upThenDownStart);
            _upThenDownBars = Deserialize<Bar>(_upThenDownFileTime, _upThenDownStart);

            FakeClient.TimeDelays.TimeScale = 0.001;
            await Task.CompletedTask;
        }

        IEnumerable<T> Deserialize<T>(DateTime fileTime, DateTime start) where T : IMarketData, new()
        {
            var path = Path.Combine(DataFetcher.RootDir, MarketDataUtils.MakeDataPath<T>(Symbol, fileTime));
            return MarketDataUtils.DeserializeData<T>(path).SkipWhile(i => i.Time < start);
        }

        [TearDown]
        public async Task TearDown()
        {
            _fakeClient.Disconnect();
            await Task.Delay(50);
        }

        [Test]
        public async Task MarketOrder_Buy_GetsFilledAtCurrentAskPrice()
        {
            // Setup
            _fakeClient = new FakeClient(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
            var order = new MarketOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.BUY,
                TotalQuantity = 50
            };

            // Test
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardBidAsks.First().Ask;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task MarketOrder_Sell_GetsFilledAtCurrentBidPrice()
        {
            // Setup
            _fakeClient = new FakeClient(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
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
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardBidAsks.First().Bid;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task LimitOrder_Buy_OverAskPrice_GetsFilledAtCurrentAskPrice()
        {
            // Setup
            _fakeClient = new FakeClient(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
            var order = new LimitOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.BUY,
                LmtPrice = _upwardBidAsks.First().Ask + 0.5,
                TotalQuantity = 50
            };

            // Test
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardBidAsks.First().Ask;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task LimitOrder_Buy_UnderAskPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeClient = new FakeClient(Symbol, _downwardStart, _downwardStart.AddMinutes(30), _downwardBars, _downwardBidAsks);
            var order = new LimitOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.BUY,
                LmtPrice = _downwardBidAsks.First().Ask - 0.02,
                TotalQuantity = 50
            };

            // Test
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _downwardBidAsks.First(ba => ba.Ask <= order.LmtPrice).Ask;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task LimitOrder_Sell_OverBidPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeClient = new FakeClient(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
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
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardBidAsks.First(ba => ba.Bid >= order.LmtPrice).Bid;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task LimitOrder_Sell_UnderBidPrice_GetsFilledAtCurrentBidPrice()
        {
            // Setup
            _fakeClient = new FakeClient(Symbol, _downwardStart, _downwardStart.AddMinutes(30), _downwardBars, _downwardBidAsks);
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
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _downwardBidAsks.First().Bid;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task StopOrder_Buy_OverAskPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeClient = new FakeClient(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
            var order = new StopOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.BUY,
                StopPrice = _upwardBidAsks.First().Ask + 0.02,
                TotalQuantity = 50
            };

            // Test
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardBidAsks.First(ba => ba.Ask >= order.StopPrice).Ask;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task StopOrder_Buy_UnderAskPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            _fakeClient = new FakeClient(Symbol, _downwardStart, _downwardStart.AddMinutes(30), _downwardBars, _downwardBidAsks);
            var order = new StopOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.BUY,
                StopPrice = _downwardBidAsks.First().Ask - 0.1,
                TotalQuantity = 50
            };

            // Test
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _downwardBidAsks.First().Ask;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task StopOrder_Sell_OverBidPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            _fakeClient = new FakeClient(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
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
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardBidAsks.First().Bid;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task StopOrder_Sell_UnderBidPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeClient = new FakeClient(Symbol, _downwardStart, _downwardStart.AddMinutes(30), _downwardBars, _downwardBidAsks);
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
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _downwardBidAsks.First(ba => ba.Bid <= order.StopPrice).Bid;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task MarketIfTouchedOrder_Buy_OverAskPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            _fakeClient = new FakeClient(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
            var order = new MarketIfTouchedOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.BUY,
                TouchPrice = _upwardBidAsks.First().Ask + 0.02,
                TotalQuantity = 50
            };

            // Test
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardBidAsks.First().Ask;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task MarketIfTouchedOrder_Buy_UnderAskPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeClient = new FakeClient(Symbol, _downwardStart, _downwardStart.AddMinutes(30), _downwardBars, _downwardBidAsks);
            var order = new MarketIfTouchedOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.BUY,
                TouchPrice = _downwardBidAsks.First().Ask - 0.03,
                TotalQuantity = 50
            };

            // Test
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _downwardBidAsks.First(ba => ba.Bid <= order.TouchPrice).Ask;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task MarketIfTouchedOrder_Sell_OverBidPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeClient = new FakeClient(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardBars, _upwardBidAsks);
            var order = new MarketIfTouchedOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.SELL,
                TouchPrice = _upwardBidAsks.First().Bid + 0.03,
                TotalQuantity = 50
            };

            var position = _fakeClient.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardBidAsks.First(ba => ba.Ask >= order.TouchPrice).Bid;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task MarketIfTouchedOrder_Sell_UnderBidPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            _fakeClient = new FakeClient(Symbol, _downwardStart, _downwardStart.AddMinutes(30), _downwardBars, _downwardBidAsks);
            var order = new MarketIfTouchedOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.SELL,
                TouchPrice = _downwardBidAsks.First().Bid - 0.02,
                TotalQuantity = 50
            };

            var position = _fakeClient.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _downwardBidAsks.First().Bid;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task TrailingStopOrder_Buy_TrailingAmout_StopPriceFallsWhenMarketFalls()
        {
            // Setup
            var end = _downwardStart.AddMinutes(3);
            _fakeClient = new FakeClient(Symbol, _downwardStart, end, _downwardBars, _downwardBidAsks);
            var order = new TrailingStopOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.BUY,
                TrailingAmount = 0.2,
                TotalQuantity = 50
            };

            // Test
            _fakeClient.Start();
            Assert.ThrowsAsync<DayIsOverException>(async () => await PlaceOrderAsync(order));

            // Assert
            var expectedStopPrice = _downwardBidAsks.Where(ba => ba.Time < end).Min(ba => ba.Ask + order.TrailingAmount);
            var actualStopPrice = order.StopPrice;
            Assert.AreEqual(expectedStopPrice, actualStopPrice, 0.0001);
            
            await Task.CompletedTask;
        }

        [Test]
        public async Task TrailingStopOrder_Buy_TrailingPercent_StopPriceFallsWhenMarketFalls()
        {
            // Setup
            var end = _downwardStart.AddMinutes(3);
            _fakeClient = new FakeClient(Symbol, _downwardStart, end, _downwardBars, _downwardBidAsks);
            var order = new TrailingStopOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.BUY,
                TrailingPercent = 0.1,
                TotalQuantity = 50
            };

            // Test
            _fakeClient.Start();
            Assert.ThrowsAsync<DayIsOverException>(async () => await PlaceOrderAsync(order));

            // Assert
            var expectedStopPrice = _downwardBidAsks.Where(ba => ba.Time < end).Min(ba => ba.Ask * order.TrailingPercent + ba.Ask);
            var actualStopPrice = order.StopPrice;
            Assert.AreEqual(expectedStopPrice, actualStopPrice, 0.0001);

            await Task.CompletedTask;
        }

        [Test]
        public async Task TrailingStopOrder_Sell_TrailingAmout_StopPriceRisesWhenMarketRises()
        {
            // Setup
            var end = _upwardStart.AddMinutes(3);
            _fakeClient = new FakeClient(Symbol, _upwardStart, end, _upwardBars, _upwardBidAsks);
            var order = new TrailingStopOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.SELL,
                TrailingAmount = 0.2,
                TotalQuantity = 50
            };

            var position = _fakeClient.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            _fakeClient.Start();
            Assert.ThrowsAsync<DayIsOverException>(async () => await PlaceOrderAsync(order));

            // Assert
            var expectedStopPrice = _upwardBidAsks.Where(ba => ba.Time < end).Max(ba => ba.Bid - order.TrailingAmount);
            var actualStopPrice = order.StopPrice;
            Assert.AreEqual(expectedStopPrice, actualStopPrice, 0.0001);

            await Task.CompletedTask;
        }

        [Test]
        public async Task TrailingStopOrder_Sell_TrailingPercent_StopPriceRisesWhenMarketRises()
        {
            // Setup
            var end = _upwardStart.AddMinutes(3);
            _fakeClient = new FakeClient(Symbol, _upwardStart, end, _upwardBars, _upwardBidAsks);
            var order = new TrailingStopOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.SELL,
                TrailingPercent = 0.1,
                TotalQuantity = 50
            };

            var position = _fakeClient.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            _fakeClient.Start();
            Assert.ThrowsAsync<DayIsOverException>(async () => await PlaceOrderAsync(order));

            // Assert
            var expectedStopPrice = _upwardBidAsks.Where(ba => ba.Time < end).Max(ba => ba.Bid - ba.Bid * order.TrailingPercent);
            var actualStopPrice = order.StopPrice;
            Assert.AreEqual(expectedStopPrice, actualStopPrice, 0.0001);

            await Task.CompletedTask;
        }

        [Test]
        public async Task TrailingStopOrder_Buy_GetsFilledWhenMarketChangesDirection_DownThenUp()
        {
            // Setup
            var end = _downThenUpStart.AddMinutes(30);
            _fakeClient = new FakeClient(Symbol, _downThenUpStart, end, _downThenUpBars, _downThenUpBidAsks);
            var order = new TrailingStopOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.BUY,
                TrailingAmount = 0.1,
                TotalQuantity = 50
            };

            // Test
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _downThenUpBidAsks.Where(ba => ba.Time < end).Min(ba => ba.Ask) + order.TrailingAmount;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.01);
        }

        [Test]
        public async Task TrailingStopOrder_Sell_GetsFilledWhenMarketChangesDirection_UpThenDown()
        {
            // Setup
            var end = _downThenUpStart.AddMinutes(30);
            _fakeClient = new FakeClient(Symbol, _upThenDownStart, end, _upThenDownBars, _upThenDownBidAsks);
            var order = new TrailingStopOrder()
            {
                Id = _fakeClient.NextValidOrderId,
                Action = OrderAction.SELL,
                TrailingAmount = 0.1,
                TotalQuantity = 50
            };

            var position = _fakeClient.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            _fakeClient.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upThenDownBidAsks.Where(ba => ba.Time < end).Max(ba => ba.Bid) - order.TrailingAmount;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.01);
        }

        async Task<OrderExecution> PlaceOrderAsync(Order order, int timeoutInMs = -1)
        {
            var tcs = new TaskCompletionSource<OrderExecution>();
            var source = new CancellationTokenSource();
            source.Token.Register(() => tcs.TrySetException(new TimeoutException($"{nameof(PlaceOrderAsync)} : {order}")));
            
            var execDetails = new Action<Contract, OrderExecution>((c, oe) =>
            {
                if(order.Id == oe.OrderId)
                    tcs.TrySetResult(oe);
            });

            var error = new Action<ErrorMessageException>(msg => tcs.TrySetException(msg));

            _fakeClient.Callbacks.ExecDetails += execDetails;
            _fakeClient.Callbacks.Error += error;

            source.CancelAfter(timeoutInMs);
            _fakeClient.PlaceOrder(_fakeClient.Contract, order);

            var passingTimeTaskId = _fakeClient.PassingTimeTask.Id;
            await Task.WhenAny(tcs.Task, _fakeClient.PassingTimeTask).ContinueWith(t => 
            { 
                _fakeClient.Callbacks.ExecDetails -= execDetails;
                _fakeClient.Callbacks.Error -= error;

                if(t.Result.Id == passingTimeTaskId)
                    tcs.TrySetException(new DayIsOverException());
            });

            return await tcs.Task;
        }

        class DayIsOverException : Exception
        {
            public override string Message => "Trading day finished without any order execution";
        }
    }
}
