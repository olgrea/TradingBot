using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backtester;
using DataStorage.Db.DbCommandFactories;
using InteractiveBrokers.Contracts;
using InteractiveBrokers.MarketData;
using InteractiveBrokers.Messages;
using InteractiveBrokers.Orders;
using NUnit.Framework;

[assembly: LevelOfParallelism(3)]

namespace Tests.Backtester
{
    [TestFixture]
    public class FakeClientSocketTests
    {
        const string Symbol = "GME";

        DateTime _downwardFileTime = new DateTime(2022, 09, 19, 11, 30, 00, DateTimeKind.Local);
        DateTime _downwardStart = new DateTime(2022, 09, 19, 11, 17, 00);
        MarketDataCollections _downwardData;

        DateTime _upwardFileTime = new DateTime(2022, 09, 19, 14, 30, 00, DateTimeKind.Local);
        DateTime _upwardStart = new DateTime(2022, 09, 19, 14, 21, 00);
        MarketDataCollections _upwardData;

        DateTime _downThenUpFileTime = new DateTime(2022, 09, 19, 15, 30, 00, DateTimeKind.Local);
        DateTime _downThenUpStart = new DateTime(2022, 09, 19, 15, 15, 00);
        MarketDataCollections _downThenUpData;

        DateTime _upThenDownFileTime = new DateTime(2022, 09, 19, 11, 30, 00, DateTimeKind.Local);
        DateTime _upThenDownStart = new DateTime(2022, 09, 19, 11, 13, 00);
        MarketDataCollections _upThenDownData;

        FakeClientSocket _fakeIBSocket;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            var barCmdFactory = new BarCommandFactory(BarLength._1Sec);
            var bidAskCmdFactory = new BidAskCommandFactory();

            _downwardData = new MarketDataCollections()
            {
                BidAsks = LoadData<BidAsk>(_downwardFileTime, _downwardStart, bidAskCmdFactory),
                Bars = LoadData<Bar>(_downwardFileTime, _downwardStart, barCmdFactory),
            };

            _upwardData = new MarketDataCollections()
            {
                BidAsks = LoadData<BidAsk>(_upwardFileTime, _upwardStart, bidAskCmdFactory),
                Bars = LoadData<Bar>(_upwardFileTime, _upwardStart, barCmdFactory),
            };

            _downThenUpData = new MarketDataCollections()
            {
                BidAsks = LoadData<BidAsk>(_downThenUpFileTime, _downThenUpStart, bidAskCmdFactory),
                Bars = LoadData<Bar>(_downThenUpFileTime, _downThenUpStart, barCmdFactory),
            };

            _upThenDownData = new MarketDataCollections()
            {
                BidAsks = LoadData<BidAsk>(_upThenDownFileTime, _upThenDownStart, bidAskCmdFactory),
                Bars = LoadData<Bar>(_upThenDownFileTime, _upThenDownStart, barCmdFactory),
            };

            FakeClientSocket.TimeDelays.TimeScale = 0.001;
            await Task.CompletedTask;
        }

        IEnumerable<T> LoadData<T>(DateTime fileTime, DateTime start, DbCommandFactory<T> dbCommandFactory) where T : IMarketData, new()
        {
            var cmd = dbCommandFactory.CreateSelectCommand(Symbol, fileTime.Date);
            return cmd.Execute().SkipWhile(i => i.Time < start);
        }

        [TearDown]
        public async Task TearDown()
        {
            _fakeIBSocket.Disconnect();
            await Task.Delay(50);
        }

        [Test]
        public async Task MarketOrder_Buy_GetsFilledAtCurrentAskPrice()
        {
            // Setup
            _fakeIBSocket = new FakeClientSocket(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardData);
            var order = new MarketOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.BUY,
                TotalQuantity = 50
            };

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardData.BidAsks.First().Ask;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task MarketOrder_Sell_GetsFilledAtCurrentBidPrice()
        {
            // Setup
            _fakeIBSocket = new FakeClientSocket(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardData);
            var order = new MarketOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.SELL,
                TotalQuantity = 50
            };

            var position = _fakeIBSocket.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardData.BidAsks.First().Bid;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task LimitOrder_Buy_OverAskPrice_GetsFilledAtCurrentAskPrice()
        {
            // Setup
            _fakeIBSocket = new FakeClientSocket(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardData);
            var order = new LimitOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.BUY,
                LmtPrice = _upwardData.BidAsks.First().Ask + 0.5,
                TotalQuantity = 50
            };

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardData.BidAsks.First().Ask;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task LimitOrder_Buy_UnderAskPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeIBSocket = new FakeClientSocket(Symbol, _downwardStart, _downwardStart.AddMinutes(30), _downwardData);
            var order = new LimitOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.BUY,
                LmtPrice = _downwardData.BidAsks.First().Ask - 0.02,
                TotalQuantity = 50
            };

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _downwardData.BidAsks.First(ba => ba.Ask <= order.LmtPrice).Ask;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task LimitOrder_Sell_OverBidPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeIBSocket = new FakeClientSocket(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardData);
            var order = new LimitOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.SELL,
                LmtPrice = _upwardData.BidAsks.First().Bid + 0.03,
                TotalQuantity = 50
            };

            var position = _fakeIBSocket.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardData.BidAsks.First(ba => ba.Bid >= order.LmtPrice).Bid;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task LimitOrder_Sell_UnderBidPrice_GetsFilledAtCurrentBidPrice()
        {
            // Setup
            _fakeIBSocket = new FakeClientSocket(Symbol, _downwardStart, _downwardStart.AddMinutes(30), _downwardData);
            var order = new LimitOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.SELL,
                LmtPrice = _downwardData.BidAsks.First().Bid - 0.1,
                TotalQuantity = 50
            };

            var position = _fakeIBSocket.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _downwardData.BidAsks.First().Bid;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task StopOrder_Buy_OverAskPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeIBSocket = new FakeClientSocket(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardData);
            var order = new StopOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.BUY,
                StopPrice = _upwardData.BidAsks.First().Ask + 0.02,
                TotalQuantity = 50
            };

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardData.BidAsks.First(ba => ba.Ask >= order.StopPrice).Ask;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task StopOrder_Buy_UnderAskPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            _fakeIBSocket = new FakeClientSocket(Symbol, _downwardStart, _downwardStart.AddMinutes(30), _downwardData);
            var order = new StopOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.BUY,
                StopPrice = _downwardData.BidAsks.First().Ask - 0.1,
                TotalQuantity = 50
            };

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _downwardData.BidAsks.First().Ask;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task StopOrder_Sell_OverBidPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            _fakeIBSocket = new FakeClientSocket(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardData);
            var order = new StopOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.SELL,
                StopPrice = _upwardData.BidAsks.First().Bid + 0.03,
                TotalQuantity = 50
            };

            var position = _fakeIBSocket.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardData.BidAsks.First().Bid;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task StopOrder_Sell_UnderBidPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeIBSocket = new FakeClientSocket(Symbol, _downwardStart, _downwardStart.AddMinutes(30), _downwardData);
            var order = new StopOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.SELL,
                StopPrice = _downwardData.BidAsks.First().Bid - 0.02,
                TotalQuantity = 50
            };

            var position = _fakeIBSocket.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _downwardData.BidAsks.First(ba => ba.Bid <= order.StopPrice).Bid;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task MarketIfTouchedOrder_Buy_OverAskPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            _fakeIBSocket = new FakeClientSocket(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardData);
            var order = new MarketIfTouchedOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.BUY,
                TouchPrice = _upwardData.BidAsks.First().Ask + 0.02,
                TotalQuantity = 50
            };

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardData.BidAsks.First().Ask;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task MarketIfTouchedOrder_Buy_UnderAskPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeIBSocket = new FakeClientSocket(Symbol, _downwardStart, _downwardStart.AddMinutes(30), _downwardData);
            var order = new MarketIfTouchedOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.BUY,
                TouchPrice = _downwardData.BidAsks.First().Ask - 0.03,
                TotalQuantity = 50
            };

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _downwardData.BidAsks.First(ba => ba.Bid <= order.TouchPrice).Ask;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task MarketIfTouchedOrder_Sell_OverBidPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            _fakeIBSocket = new FakeClientSocket(Symbol, _upwardStart, _upwardStart.AddMinutes(30), _upwardData);
            var order = new MarketIfTouchedOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.SELL,
                TouchPrice = _upwardData.BidAsks.First().Bid + 0.03,
                TotalQuantity = 50
            };

            var position = _fakeIBSocket.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upwardData.BidAsks.First(ba => ba.Ask >= order.TouchPrice).Bid;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task MarketIfTouchedOrder_Sell_UnderBidPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            _fakeIBSocket = new FakeClientSocket(Symbol, _downwardStart, _downwardStart.AddMinutes(30), _downwardData);
            var order = new MarketIfTouchedOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.SELL,
                TouchPrice = _downwardData.BidAsks.First().Bid - 0.02,
                TotalQuantity = 50
            };

            var position = _fakeIBSocket.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _downwardData.BidAsks.First().Bid;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task TrailingStopOrder_Buy_TrailingAmout_StopPriceFallsWhenMarketFalls()
        {
            // Setup
            var end = _downwardStart.AddMinutes(3);
            _fakeIBSocket = new FakeClientSocket(Symbol, _downwardStart, end, _downwardData);
            var order = new TrailingStopOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.BUY,
                TrailingAmount = 0.2,
                TotalQuantity = 50
            };

            // Test
            _fakeIBSocket.Start();
            Assert.ThrowsAsync<DayIsOverException>(async () => await PlaceOrderAsync(order));

            // Assert
            var expectedStopPrice = _downwardData.BidAsks.Where(ba => ba.Time < end).Min(ba => ba.Ask + order.TrailingAmount);
            var actualStopPrice = order.StopPrice;
            Assert.AreEqual(expectedStopPrice, actualStopPrice, 0.0001);
            
            await Task.CompletedTask;
        }

        [Test]
        public async Task TrailingStopOrder_Buy_TrailingPercent_StopPriceFallsWhenMarketFalls()
        {
            // Setup
            var end = _downwardStart.AddMinutes(3);
            _fakeIBSocket = new FakeClientSocket(Symbol, _downwardStart, end, _downwardData);
            var order = new TrailingStopOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.BUY,
                TrailingPercent = 0.1,
                TotalQuantity = 50
            };

            // Test
            _fakeIBSocket.Start();
            Assert.ThrowsAsync<DayIsOverException>(async () => await PlaceOrderAsync(order));

            // Assert
            var expectedStopPrice = _downwardData.BidAsks.Where(ba => ba.Time < end).Min(ba => ba.Ask * order.TrailingPercent + ba.Ask);
            var actualStopPrice = order.StopPrice;
            Assert.AreEqual(expectedStopPrice, actualStopPrice, 0.0001);

            await Task.CompletedTask;
        }

        [Test]
        public async Task TrailingStopOrder_Sell_TrailingAmout_StopPriceRisesWhenMarketRises()
        {
            // Setup
            var end = _upwardStart.AddMinutes(3);
            _fakeIBSocket = new FakeClientSocket(Symbol, _upwardStart, end, _upwardData);
            var order = new TrailingStopOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.SELL,
                TrailingAmount = 0.2,
                TotalQuantity = 50
            };

            var position = _fakeIBSocket.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            _fakeIBSocket.Start();
            Assert.ThrowsAsync<DayIsOverException>(async () => await PlaceOrderAsync(order));

            // Assert
            var expectedStopPrice = _upwardData.BidAsks.Where(ba => ba.Time < end).Max(ba => ba.Bid - order.TrailingAmount);
            var actualStopPrice = order.StopPrice;
            Assert.AreEqual(expectedStopPrice, actualStopPrice, 0.0001);

            await Task.CompletedTask;
        }

        [Test]
        public async Task TrailingStopOrder_Sell_TrailingPercent_StopPriceRisesWhenMarketRises()
        {
            // Setup
            var end = _upwardStart.AddMinutes(3);
            _fakeIBSocket = new FakeClientSocket(Symbol, _upwardStart, end, _upwardData);
            var order = new TrailingStopOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.SELL,
                TrailingPercent = 0.1,
                TotalQuantity = 50
            };

            var position = _fakeIBSocket.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            _fakeIBSocket.Start();
            Assert.ThrowsAsync<DayIsOverException>(async () => await PlaceOrderAsync(order));

            // Assert
            var expectedStopPrice = _upwardData.BidAsks.Where(ba => ba.Time < end).Max(ba => ba.Bid - ba.Bid * order.TrailingPercent);
            var actualStopPrice = order.StopPrice;
            Assert.AreEqual(expectedStopPrice, actualStopPrice, 0.0001);

            await Task.CompletedTask;
        }

        [Test]
        public async Task TrailingStopOrder_Buy_GetsFilledWhenMarketChangesDirection_DownThenUp()
        {
            // Setup
            var end = _downThenUpStart.AddMinutes(30);
            _fakeIBSocket = new FakeClientSocket(Symbol, _downThenUpStart, end, _downThenUpData);
            var order = new TrailingStopOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.BUY,
                TrailingAmount = 0.1,
                TotalQuantity = 50
            };

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _downThenUpData.BidAsks.Where(ba => ba.Time < end).Min(ba => ba.Ask) + order.TrailingAmount;
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.01);
        }

        [Test]
        public async Task TrailingStopOrder_Sell_GetsFilledWhenMarketChangesDirection_UpThenDown()
        {
            // Setup
            var end = _downThenUpStart.AddMinutes(30);
            _fakeIBSocket = new FakeClientSocket(Symbol, _upThenDownStart, end, _upThenDownData);
            var order = new TrailingStopOrder()
            {
                Id = _fakeIBSocket.NextValidOrderId,
                Action = OrderAction.SELL,
                TrailingAmount = 0.1,
                TotalQuantity = 50
            };

            var position = _fakeIBSocket.Account.Positions.First();
            position.PositionAmount = 50;
            position.AverageCost = 28.00;

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var expectedPrice = _upThenDownData.BidAsks.Where(ba => ba.Time < end).Max(ba => ba.Bid) - order.TrailingAmount;
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

            _fakeIBSocket.Callbacks.ExecDetails += execDetails;
            _fakeIBSocket.Callbacks.Error += error;

            source.CancelAfter(timeoutInMs);
            _fakeIBSocket.PlaceOrder(_fakeIBSocket.Contract, order);

            var passingTimeTaskId = _fakeIBSocket.PassingTimeTask.Id;
            await Task.WhenAny(tcs.Task, _fakeIBSocket.PassingTimeTask).ContinueWith(t => 
            { 
                _fakeIBSocket.Callbacks.ExecDetails -= execDetails;
                _fakeIBSocket.Callbacks.Error -= error;

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
