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

        Backtester _backtester;
        ILogger _logger;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            ConfigurationItemFactory.Default.Targets.RegisterDefinition("NUnitLogger", typeof(TradingBotV2.Tests.NunitTargetLogger));
            _logger = LogManager.GetLogger($"{nameof(BacktesterOrderManagerTests)}", typeof(TradingBotV2.Tests.NunitTargetLogger));
            await Task.CompletedTask;
        }

        public async Task TearDown()
        {
            if(_backtester != null)
                await _backtester.DisposeAsync().AsTask();
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
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = CreateBacktester(timerange.Item1.Date, timerange.Item1, timerange.Item2);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var order = new MarketOrder()
            {
                Action = OrderAction.BUY,
                TotalQuantity = 50
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecution(order);

            // Assert
            Assert.NotNull(result);
            Assert.LessOrEqual(result.OrderExecution.Time - timeOfOrderPlacement, TimeSpan.FromSeconds(1));

            var bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Ask).Min();
            var highest = bidAsks.Select(ba => ba.Ask).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task MarketOrder_Sell_GetsFilledAtCurrentBidPrice()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = new Backtester(timerange.Item1.Date, timerange.Item1.TimeOfDay, timerange.Item2.TimeOfDay, _logger);           
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var order = new MarketOrder()
            {
                Action = OrderAction.SELL,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position()
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecution(order);

            // Assert
            Assert.NotNull(result);
            Assert.LessOrEqual(result.OrderExecution.Time - timeOfOrderPlacement, TimeSpan.FromSeconds(1));

            var bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Bid).Min();
            var highest = bidAsks.Select(ba => ba.Bid).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task LimitOrder_Buy_OverAskPrice_GetsFilledAtCurrentAskPrice()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = new Backtester(timerange.Item1.Date, timerange.Item1.TimeOfDay, timerange.Item2.TimeOfDay, _logger);            
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);
            
            var bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, timeOfOrderPlacement);
            var limitPrice = bidAsks.Select(ba => ba.Ask).Average() + 0.5;
            var order = new LimitOrder()
            {
                Action = OrderAction.BUY,
                LmtPrice = limitPrice,
                TotalQuantity = 50
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecution(order);

            // Assert
            Assert.NotNull(result);
            Assert.LessOrEqual(result.OrderExecution.Time - timeOfOrderPlacement, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Ask).Min();
            var highest = bidAsks.Select(ba => ba.Ask).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }

        [Test]
        public async Task LimitOrder_Buy_UnderAskPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            (DateTime, DateTime) timerange = _downwardTimeRange;
            _backtester = new Backtester(timerange.Item1.Date, timerange.Item1.TimeOfDay, timerange.Item2.TimeOfDay, _logger);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, timeOfOrderPlacement);
            var limitPrice = bidAsks.Select(ba => ba.Ask).Average() - 0.5;
            var order = new LimitOrder()
            {
                Action = OrderAction.BUY,
                LmtPrice = limitPrice,
                TotalQuantity = 50
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecution(order);

            // Assert
            Assert.NotNull(result);
            Assert.Greater(result.OrderExecution.Time - timeOfOrderPlacement, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Ask).Min();
            var highest = bidAsks.Select(ba => ba.Ask).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task LimitOrder_Sell_OverBidPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = new Backtester(timerange.Item1.Date, timerange.Item1.TimeOfDay, timerange.Item2.TimeOfDay, _logger);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, timeOfOrderPlacement);
            var limitPrice = bidAsks.Select(ba => ba.Bid).Average() + 0.5;
            var order = new LimitOrder()
            {
                Action = OrderAction.SELL,
                LmtPrice = limitPrice,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position()
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecution(order);

            // Assert
            Assert.NotNull(result);
            Assert.Greater(result.OrderExecution.Time - timeOfOrderPlacement, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Bid).Min();
            var highest = bidAsks.Select(ba => ba.Bid).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task LimitOrder_Sell_UnderBidPrice_GetsFilledAtCurrentBidPrice()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = new Backtester(timerange.Item1.Date, timerange.Item1.TimeOfDay, timerange.Item2.TimeOfDay, _logger);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, timeOfOrderPlacement);
            var limitPrice = bidAsks.Select(ba => ba.Bid).Average() - 0.5;
            var order = new LimitOrder()
            {
                Action = OrderAction.SELL,
                LmtPrice = limitPrice,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position()
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecution(order);

            // Assert
            Assert.NotNull(result);
            Assert.LessOrEqual(result.OrderExecution.Time - timeOfOrderPlacement, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Bid).Min();
            var highest = bidAsks.Select(ba => ba.Bid).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task StopOrder_Buy_OverAskPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = new Backtester(timerange.Item1.Date, timerange.Item1.TimeOfDay, timerange.Item2.TimeOfDay, _logger);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, timeOfOrderPlacement);
            var stopPrice = bidAsks.Select(ba => ba.Ask).Average() + 0.5;
            var order = new StopOrder()
            {
                Action = OrderAction.BUY,
                StopPrice = stopPrice,
                TotalQuantity = 50
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecution(order);

            // Assert
            Assert.NotNull(result);
            Assert.Greater(result.OrderExecution.Time - timeOfOrderPlacement, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Ask).Min();
            var highest = bidAsks.Select(ba => ba.Ask).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task StopOrder_Buy_UnderAskPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = new Backtester(timerange.Item1.Date, timerange.Item1.TimeOfDay, timerange.Item2.TimeOfDay, _logger);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, timeOfOrderPlacement);
            var stopPrice = bidAsks.Select(ba => ba.Ask).Average() - 0.5;
            var order = new StopOrder()
            {
                Action = OrderAction.BUY,
                StopPrice = stopPrice,
                TotalQuantity = 50
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecution(order);

            // Assert
            Assert.NotNull(result);
            Assert.LessOrEqual(result.OrderExecution.Time - timeOfOrderPlacement, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Ask).Min();
            var highest = bidAsks.Select(ba => ba.Ask).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task StopOrder_Sell_OverBidPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = new Backtester(timerange.Item1.Date, timerange.Item1.TimeOfDay, timerange.Item2.TimeOfDay, _logger);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, timeOfOrderPlacement);
            var stopPrice = bidAsks.Select(ba => ba.Bid).Average() + 0.5;
            var order = new StopOrder()
            {
                Action = OrderAction.SELL,
                StopPrice = stopPrice,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position()
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecution(order);

            // Assert
            Assert.NotNull(result);
            Assert.LessOrEqual(result.OrderExecution.Time - timeOfOrderPlacement, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Bid).Min();
            var highest = bidAsks.Select(ba => ba.Bid).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task StopOrder_Sell_UnderBidPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            (DateTime, DateTime) timerange = _downwardTimeRange;
            _backtester = new Backtester(timerange.Item1.Date, timerange.Item1.TimeOfDay, timerange.Item2.TimeOfDay, _logger);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, timeOfOrderPlacement);
            var stopPrice = bidAsks.Select(ba => ba.Bid).Average() - 0.5;
            var order = new StopOrder()
            {
                Action = OrderAction.SELL,
                StopPrice = stopPrice,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position()
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecution(order);

            // Assert
            Assert.NotNull(result);
            Assert.Greater(result.OrderExecution.Time - timeOfOrderPlacement, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Bid).Min();
            var highest = bidAsks.Select(ba => ba.Bid).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task MarketIfTouchedOrder_Buy_OverAskPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = new Backtester(timerange.Item1.Date, timerange.Item1.TimeOfDay, timerange.Item2.TimeOfDay, _logger);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, timeOfOrderPlacement);
            var touchPrice = bidAsks.Select(ba => ba.Ask).Average() + 0.5;
            var order = new MarketIfTouchedOrder()
            {
                Action = OrderAction.BUY,
                TouchPrice = touchPrice,
                TotalQuantity = 50
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecution(order);

            // Assert
            Assert.NotNull(result);
            Assert.LessOrEqual(result.OrderExecution.Time - timeOfOrderPlacement, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Ask).Min();
            var highest = bidAsks.Select(ba => ba.Ask).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task MarketIfTouchedOrder_Buy_UnderAskPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            (DateTime, DateTime) timerange = _downwardTimeRange;
            _backtester = new Backtester(timerange.Item1.Date, timerange.Item1.TimeOfDay, timerange.Item2.TimeOfDay, _logger);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, timeOfOrderPlacement);
            var touchPrice = bidAsks.Select(ba => ba.Ask).Average() - 0.5;
            var order = new MarketIfTouchedOrder()
            {
                Action = OrderAction.BUY,
                TouchPrice = touchPrice,
                TotalQuantity = 50
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecution(order);

            // Assert
            Assert.NotNull(result);
            Assert.Greater(result.OrderExecution.Time - timeOfOrderPlacement, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Ask).Min();
            var highest = bidAsks.Select(ba => ba.Ask).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task MarketIfTouchedOrder_Sell_OverBidPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = new Backtester(timerange.Item1.Date, timerange.Item1.TimeOfDay, timerange.Item2.TimeOfDay, _logger);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, timeOfOrderPlacement);
            var touchPrice = bidAsks.Select(ba => ba.Bid).Average() + 0.5;
            var order = new MarketIfTouchedOrder()
            {
                Action = OrderAction.SELL,
                TouchPrice = touchPrice,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position()
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecution(order);

            // Assert
            Assert.NotNull(result);
            Assert.Greater(result.OrderExecution.Time - timeOfOrderPlacement, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Bid).Min();
            var highest = bidAsks.Select(ba => ba.Bid).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task MarketIfTouchedOrder_Sell_UnderBidPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = new Backtester(timerange.Item1.Date, timerange.Item1.TimeOfDay, timerange.Item2.TimeOfDay, _logger);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, timeOfOrderPlacement);
            var touchPrice = bidAsks.Select(ba => ba.Bid).Average() - 0.5;
            var order = new MarketIfTouchedOrder()
            {
                Action = OrderAction.SELL,
                TouchPrice = touchPrice,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position()
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecution(order);

            // Assert
            Assert.NotNull(result);
            Assert.LessOrEqual(result.OrderExecution.Time - timeOfOrderPlacement, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Bid).Min();
            var highest = bidAsks.Select(ba => ba.Bid).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task TrailingStopOrder_Buy_TrailingAmout_StopPriceFallsWhenMarketFalls()
        {
            // Setup
            (DateTime, DateTime) timerange = _downwardTimeRange;
            _backtester = new Backtester(timerange.Item1.Date, timerange.Item1.TimeOfDay, timerange.Item2.TimeOfDay, _logger);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var order = new TrailingStopOrder()
            {
                Action = OrderAction.BUY,
                TrailingAmount = 0.2,
                TotalQuantity = 50
            };

            var backtestingTask = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var initialBidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, timeOfOrderPlacement);
            var initialStopPrice = initialBidAsks.Select(ba => ba.Ask).Average() + order.TrailingAmount;
            await backtestingTask;

            // Assert
            DateTime current = timerange.Item2;
            var bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, current);
            while(!bidAsks.Any() && current > timerange.Item1)
            {
                current.AddSeconds(-1);
                bidAsks = await _backtester.MarketData.BidAsks.GetAsync(Ticker, current);
            }

            var expectedStopPrice = bidAsks.Min(ba => ba.Ask + order.TrailingAmount);
            var actualStopPrice = order.StopPrice;
            Assert.AreEqual(expectedStopPrice, actualStopPrice, 0.0001);

            //// Setup
            //var end = _downwardStart.AddMinutes(3);
            //_fakeIBSocket = new BacktesterClientSocket(Ticker, _downwardStart, end, _downwardData);
            //var order = new TrailingStopOrder()
            //{
            //    Id = _fakeIBSocket.NextValidOrderId,
            //    Action = OrderAction.BUY,
            //    TrailingAmount = 0.2,
            //    TotalQuantity = 50
            //};

            //// Test
            //_fakeIBSocket.Start();
            //Assert.ThrowsAsync<DayIsOverException>(async () => await PlaceOrderAsync(order));

            //// Assert
            //var expectedStopPrice = _downwardData.BidAsks.Where(ba => ba.Time < end).Min(ba => ba.Ask + order.TrailingAmount);
            //var actualStopPrice = order.StopPrice;
            //Assert.AreEqual(expectedStopPrice, actualStopPrice, 0.0001);

            //await Task.CompletedTask;
        }
        /*
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

        async Task WaitUntilTimeIsReached(DateTime dateTime)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _backtester.ProgressHandler = new Action<BacktesterProgress>(bp =>
            {
                if (bp.CurrentTime >= dateTime)
                    tcs.TrySetResult();
            });
            await tcs.Task;
        }
    }
}
