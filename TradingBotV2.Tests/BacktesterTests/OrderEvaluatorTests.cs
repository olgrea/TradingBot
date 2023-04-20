using NLog.Config;
using NLog;
using NUnit.Framework;
using TradingBotV2.Backtesting;
using TradingBotV2.IBKR;
using TradingBotV2.Broker.Orders;
using TradingBotV2.Broker.Accounts;

namespace BacktesterTests
{
    internal class OrderEvaluatorTests
    {
        const string TestDbPath = @"C:\tradingbot\db\tests.sqlite3";
        const string Ticker = "SPY";

        (DateTime, DateTime) _downwardTimeRange =   (new DateTime(2023, 04, 10, 11, 50, 00), new DateTime(2023, 04, 10, 12, 10, 00));
        (DateTime, DateTime) _upwardTimeRange =     (new DateTime(2023, 04, 10, 12, 30, 00), new DateTime(2023, 04, 10, 12, 45, 00));
        (DateTime, DateTime) _downThenUpTimeRange = (new DateTime(2023, 04, 10, 12, 00, 00), new DateTime(2023, 04, 10, 12, 20, 00));
        (DateTime, DateTime) _upThenDownTimeRange = (new DateTime(2023, 04, 10, 10, 00, 00), new DateTime(2023, 04, 10, 10, 30, 00));

        ILogger _logger;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            ConfigurationItemFactory.Default.Targets.RegisterDefinition("NUnitLogger", typeof(TradingBotV2.Tests.NunitTargetLogger));
            _logger = LogManager.GetLogger($"{nameof(BacktesterOrderManagerTests)}", typeof(TradingBotV2.Tests.NunitTargetLogger));
            await Task.CompletedTask;
        }

        Backtester CreateBacktester(DateTime date, DateTime start, DateTime end)
        {
            var backtester = new Backtester(date.Date, start.TimeOfDay, end.TimeOfDay, _logger);
            var hdp = (IBHistoricalDataProvider)backtester.HistoricalDataProvider;
            hdp.DbPath = TestDbPath;
            return backtester;
        }

        [Test]
        public async Task MarketOrder_Buy_GetsFilledAtCurrentAskPrice()
        {
            // Setup
            var backtester = CreateBacktester(_upwardTimeRange.Item1.Date, _upwardTimeRange.Item1, _upwardTimeRange.Item2);
            var order = new MarketOrder()
            {
                Action = OrderAction.BUY,
                TotalQuantity = 50
            };

            DateTime timeOfOrderPlacement = _upwardTimeRange.Item1.AddMinutes(5);
            var expectedPrice = (await backtester.MarketData.BidAsks.GetAsync(Ticker, timeOfOrderPlacement)).First().Ask;

            var tcs = new TaskCompletionSource();
            backtester.ProgressHandler = new Progress<BacktesterProgress>(bp =>
            {
                if (bp.CurrentTime >= timeOfOrderPlacement)
                    tcs.TrySetResult();
            });

            // Test
            _ = backtester.Start();

            await tcs.Task;
            var result = (await backtester.OrderManager.PlaceOrderAsync(Ticker, order)) as OrderExecutedResult;

            // Assert
            Assert.NotNull(result);
            if(result != null)
            {
                var actualPrice = result.OrderExecution.AvgPrice;
                Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
            }
        }


        /*
        [Test]
        public async Task MarketOrder_Sell_GetsFilledAtCurrentBidPrice()
        {
            // Setup
            var backtester = new Backtester(_upwardTimeRange.Item1.Date, _upwardTimeRange.Item1.TimeOfDay, _upwardTimeRange.Item2.TimeOfDay, _logger);
            var order = new MarketOrder()
            {
                Action = OrderAction.SELL,
                TotalQuantity = 50
            };

            var position = backtester.Account.Positions[Ticker] = new Position()
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            var expectedPrice = _upwardData.BidAsks.First().Bid;

            // Test
            _fakeIBSocket.Start();
            var orderExecution = await PlaceOrderAsync(order);

            // Assert
            Assert.NotNull(orderExecution);
            var actualPrice = orderExecution.AvgPrice;
            Assert.AreEqual(expectedPrice, actualPrice, 0.0001);
        }

        [Test]
        public async Task LimitOrder_Buy_OverAskPrice_GetsFilledAtCurrentAskPrice()
        {
            // Setup
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _upwardStart, _upwardStart.AddMinutes(30), _upwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _downwardStart, _downwardStart.AddMinutes(30), _downwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _upwardStart, _upwardStart.AddMinutes(30), _upwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _downwardStart, _downwardStart.AddMinutes(30), _downwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _upwardStart, _upwardStart.AddMinutes(30), _upwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _downwardStart, _downwardStart.AddMinutes(30), _downwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _upwardStart, _upwardStart.AddMinutes(30), _upwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _downwardStart, _downwardStart.AddMinutes(30), _downwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _upwardStart, _upwardStart.AddMinutes(30), _upwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _downwardStart, _downwardStart.AddMinutes(30), _downwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _upwardStart, _upwardStart.AddMinutes(30), _upwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _downwardStart, _downwardStart.AddMinutes(30), _downwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _downwardStart, end, _downwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _downwardStart, end, _downwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _upwardStart, end, _upwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _upwardStart, end, _upwardData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _downThenUpStart, end, _downThenUpData);
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
            _fakeIBSocket = new BacktesterClientSocket(Ticker, _upThenDownStart, end, _upThenDownData);
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

        */

    }
}
