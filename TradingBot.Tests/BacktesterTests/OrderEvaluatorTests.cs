using NUnit.Framework;
using TradingBot.Backtesting;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Tests;

namespace BacktesterTests
{
    internal class OrderEvaluatorTests
    {
        const string Ticker = "SPY";

        (DateTime, DateTime) _downwardTimeRange =   (new DateTime(2023, 04, 10, 11, 50, 00), new DateTime(2023, 04, 10, 12, 10, 00));
        (DateTime, DateTime) _upwardTimeRange =     (new DateTime(2023, 04, 10, 12, 30, 00), new DateTime(2023, 04, 10, 12, 45, 00));
        (DateTime, DateTime) _downThenUpTimeRange = (new DateTime(2023, 04, 10, 12, 00, 00), new DateTime(2023, 04, 10, 12, 20, 00));
        (DateTime, DateTime) _upThenDownTimeRange = (new DateTime(2023, 04, 10, 10, 00, 00), new DateTime(2023, 04, 10, 10, 30, 00));

        Backtester _backtester;

        [TearDown]
        public async Task TearDown()
        {
            if(_backtester != null)
            {
                await _backtester.DisposeAsync().AsTask();
            }
        }

        async Task<Backtester> CreateBacktesterAndConnect((DateTime, DateTime) timeRange)
        {
            var backtester = TestsUtils.CreateBacktester(timeRange.Item1, timeRange.Item2);
            await backtester.ConnectAsync();
            return backtester;
        }

        [Test]
        public async Task MarketOrder_Buy_GetsFilledAtCurrentAskPrice()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);

            var order = new MarketOrder()
            {
                Action = OrderAction.BUY,
                TotalQuantity = 50
            };

            _ = _backtester.Start();

            // Test
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var execResult = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            Assert.NotNull(placedResult);
            Assert.NotNull(execResult);
            Assert.LessOrEqual(execResult.OrderExecution.Time - placedResult.Time , TimeSpan.FromSeconds(1));

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, execResult.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Ask).Min();
            var highest = bidAsks.Select(ba => ba.Ask).Max();
            var actualPrice = execResult.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task MarketOrder_Sell_GetsFilledAtCurrentBidPrice()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);

            var order = new MarketOrder()
            {
                Action = OrderAction.SELL,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position(Ticker)
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            _ = _backtester.Start();

            // Test
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var execResult = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            Assert.NotNull(placedResult);
            Assert.NotNull(execResult);
            Assert.LessOrEqual(execResult.OrderExecution.Time - placedResult.Time, TimeSpan.FromSeconds(1));

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, execResult.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Bid).Min();
            var highest = bidAsks.Select(ba => ba.Bid).Max();
            var actualPrice = execResult.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task LimitOrder_Buy_OverAskPrice_GetsFilledAtCurrentAskPrice()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);
            
            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement);
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
            var placedResults = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var execResult = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            Assert.NotNull(execResult);
            Assert.LessOrEqual(execResult.OrderExecution.Time - placedResults.Time, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, execResult.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Ask).Min();
            var highest = bidAsks.Select(ba => ba.Ask).Max();
            var actualPrice = execResult.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }

        [Test]
        public async Task LimitOrder_Buy_UnderAskPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            (DateTime, DateTime) timerange = _downwardTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement);
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
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var execResult = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            Assert.NotNull(execResult);
            Assert.Greater(execResult.OrderExecution.Time - placedResult.Time, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, execResult.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Ask).Min();
            var highest = bidAsks.Select(ba => ba.Ask).Max();
            var actualPrice = execResult.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task LimitOrder_Sell_OverBidPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement);
            var limitPrice = bidAsks.Select(ba => ba.Bid).Average() + 0.5;
            var order = new LimitOrder()
            {
                Action = OrderAction.SELL,
                LmtPrice = limitPrice,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position(Ticker)
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var execResult = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            Assert.NotNull(execResult);
            Assert.Greater(execResult.OrderExecution.Time - placedResult.Time, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, execResult.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Bid).Min();
            var highest = bidAsks.Select(ba => ba.Bid).Max();
            var actualPrice = execResult.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task LimitOrder_Sell_UnderBidPrice_GetsFilledAtCurrentBidPrice()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement);
            var limitPrice = bidAsks.Select(ba => ba.Bid).Average() - 0.5;
            var order = new LimitOrder()
            {
                Action = OrderAction.SELL,
                LmtPrice = limitPrice,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position(Ticker)
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var execResult = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            Assert.NotNull(execResult);
            Assert.LessOrEqual(execResult.OrderExecution.Time - placedResult.Time, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, execResult.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Bid).Min();
            var highest = bidAsks.Select(ba => ba.Bid).Max();
            var actualPrice = execResult.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task StopOrder_Buy_OverAskPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement);
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
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var execResult = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            Assert.NotNull(execResult);
            Assert.Greater(execResult.OrderExecution.Time - placedResult.Time, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, execResult.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Ask).Min();
            var highest = bidAsks.Select(ba => ba.Ask).Max();
            var actualPrice = execResult.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task StopOrder_Buy_UnderAskPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement);
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
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var execResult = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            Assert.NotNull(execResult);
            Assert.LessOrEqual(execResult.OrderExecution.Time - placedResult.Time, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, execResult.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Ask).Min();
            var highest = bidAsks.Select(ba => ba.Ask).Max();
            var actualPrice = execResult.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task StopOrder_Sell_OverBidPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement);
            var stopPrice = bidAsks.Select(ba => ba.Bid).Average() + 0.5;
            var order = new StopOrder()
            {
                Action = OrderAction.SELL,
                StopPrice = stopPrice,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position(Ticker)
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var execResult = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            Assert.NotNull(execResult);
            Assert.LessOrEqual(execResult.OrderExecution.Time - placedResult.Time, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, execResult.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Bid).Min();
            var highest = bidAsks.Select(ba => ba.Bid).Max();
            var actualPrice = execResult.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task StopOrder_Sell_UnderBidPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            (DateTime, DateTime) timerange = _downwardTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement);
            var stopPrice = bidAsks.Select(ba => ba.Bid).Average() - 0.5;
            var order = new StopOrder()
            {
                Action = OrderAction.SELL,
                StopPrice = stopPrice,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position(Ticker)
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var execResult = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            Assert.NotNull(execResult);
            Assert.Greater(execResult.OrderExecution.Time - placedResult.Time, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, execResult.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Bid).Min();
            var highest = bidAsks.Select(ba => ba.Bid).Max();
            var actualPrice = execResult.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task MarketIfTouchedOrder_Buy_OverAskPrice_GetsFilledAtCurrentPrice()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement);
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
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var execResult = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            Assert.NotNull(execResult);
            Assert.LessOrEqual(execResult.OrderExecution.Time - placedResult.Time, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, execResult.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Ask).Min();
            var highest = bidAsks.Select(ba => ba.Ask).Max();
            var actualPrice = execResult.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task MarketIfTouchedOrder_Buy_UnderAskPrice_GetsFilledWhenPriceIsReached()
        {
            // Setup
            (DateTime, DateTime) timerange = _downwardTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement);
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
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            Assert.NotNull(result);
            Assert.Greater(result.OrderExecution.Time - placedResult.Time, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, result.OrderExecution.Time);
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
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement);
            var touchPrice = bidAsks.Select(ba => ba.Bid).Average() + 0.5;
            var order = new MarketIfTouchedOrder()
            {
                Action = OrderAction.SELL,
                TouchPrice = touchPrice,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position(Ticker)
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            Assert.NotNull(result);
            Assert.Greater(result.OrderExecution.Time - placedResult.Time, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, result.OrderExecution.Time);
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
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement);
            var touchPrice = bidAsks.Select(ba => ba.Bid).Average() - 0.5;
            var order = new MarketIfTouchedOrder()
            {
                Action = OrderAction.SELL,
                TouchPrice = touchPrice,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position(Ticker)
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            Assert.NotNull(result);
            Assert.LessOrEqual(result.OrderExecution.Time - placedResult.Time, TimeSpan.FromSeconds(1));

            bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, result.OrderExecution.Time);
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
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement, timerange.Item2);
            
            var trailingAmount = 0.5;
            var initialStopPrice = bidAsks.Where(ba => ba.Time == timeOfOrderPlacement).Average(ba => ba.Ask) + trailingAmount;
            var order = new TrailingStopOrder()
            {
                Action = OrderAction.BUY,
                TrailingAmount = trailingAmount,
                TrailingAmountUnits = TrailingAmountUnits.Absolute,
                StopPrice = initialStopPrice,
                TotalQuantity = 50
            };

            var expectedStopPrice = bidAsks.SkipWhile(ba => ba.Time < timeOfOrderPlacement).Min(ba => ba.Ask + order.TrailingAmount.Value);

            var backtestingTask = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            await backtestingTask;

            // Assert
            var actualStopPrice = order.StopPrice!.Value;
            Assert.AreEqual(expectedStopPrice, actualStopPrice, 0.01);
        }
        
        [Test]
        public async Task TrailingStopOrder_Buy_TrailingPercent_StopPriceFallsWhenMarketFalls()
        {
            // Setup
            (DateTime, DateTime) timerange = _downwardTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement, timerange.Item2);

            var trailingAmount = 0.05; // %
            var initialStopPrice = bidAsks.Where(ba => ba.Time == timeOfOrderPlacement).Average(ba => ba.Ask) * (1 + trailingAmount);
            var order = new TrailingStopOrder()
            {
                Action = OrderAction.BUY,
                StopPrice = initialStopPrice,
                TrailingAmount = trailingAmount,
                TrailingAmountUnits = TrailingAmountUnits.Percent,
                TotalQuantity = 50
            };

            var expectedStopPrice = bidAsks.SkipWhile(ba => ba.Time < timeOfOrderPlacement).Min(ba => ba.Ask * (1 + order.TrailingAmount!.Value));

            var backtestingTask = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            await backtestingTask;

            // Assert
            var actualStopPrice = order.StopPrice!.Value;
            Assert.AreEqual(expectedStopPrice, actualStopPrice, 0.01);
        }
        
        [Test]
        public async Task TrailingStopOrder_Sell_TrailingAmout_StopPriceRisesWhenMarketRises()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement, timerange.Item2);

            var trailingAmount = 0.5;
            var initialStopPrice = bidAsks.Where(ba => ba.Time == timeOfOrderPlacement).Average(ba => ba.Bid) - trailingAmount;
            var order = new TrailingStopOrder()
            {
                Action = OrderAction.SELL,
                TrailingAmount = trailingAmount,
                TrailingAmountUnits = TrailingAmountUnits.Absolute,
                StopPrice = initialStopPrice,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position(Ticker)
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            var expectedStopPrice = bidAsks.SkipWhile(ba => ba.Time < timeOfOrderPlacement).Max(ba => ba.Bid - order.TrailingAmount.Value);

            var backtestingTask = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            await backtestingTask;

            // Assert
            var actualStopPrice = order.StopPrice!.Value;
            Assert.AreEqual(expectedStopPrice, actualStopPrice, 0.01);
        }
        
        [Test]
        public async Task TrailingStopOrder_Sell_TrailingPercent_StopPriceRisesWhenMarketRises()
        {
            // Setup
            (DateTime, DateTime) timerange = _upwardTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement, timerange.Item2);

            var trailingAmount = 0.5;
            var initialStopPrice = bidAsks.Where(ba => ba.Time == timeOfOrderPlacement).Average(ba => ba.Bid) * (1 - trailingAmount);
            var order = new TrailingStopOrder()
            {
                Action = OrderAction.SELL,
                TrailingAmount = trailingAmount,
                TrailingAmountUnits = TrailingAmountUnits.Percent,
                StopPrice = initialStopPrice,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position(Ticker)
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            var expectedStopPrice = bidAsks.SkipWhile(ba => ba.Time < timeOfOrderPlacement).Max(ba => ba.Bid * (1 - order.TrailingAmount.Value));

            var backtestingTask = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            await backtestingTask;

            // Assert
            var actualStopPrice = order.StopPrice!.Value;
            Assert.AreEqual(expectedStopPrice, actualStopPrice, 0.01);
        }
        
        [Test]
        public async Task TrailingStopOrder_Buy_GetsFilledWhenMarketChangesDirection_DownThenUp()
        {
            // Setup
            (DateTime, DateTime) timerange = _downThenUpTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(5);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement, timerange.Item2);

            var trailingAmount = 0.2;
            var initialStopPrice = bidAsks.Where(ba => ba.Time == timeOfOrderPlacement).Average(ba => ba.Ask) + trailingAmount;
            var order = new TrailingStopOrder()
            {
                Action = OrderAction.BUY,
                TrailingAmount = trailingAmount,
                TrailingAmountUnits = TrailingAmountUnits.Absolute,
                StopPrice = initialStopPrice,
                TotalQuantity = 50
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Ask).Min();
            var highest = bidAsks.Select(ba => ba.Ask).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.GreaterOrEqual(actualPrice, order.StopPrice.Value);
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }
        
        [Test]
        public async Task TrailingStopOrder_Sell_GetsFilledWhenMarketChangesDirection_UpThenDown()
        {
            // Setup
            (DateTime, DateTime) timerange = _upThenDownTimeRange;
            _backtester = await CreateBacktesterAndConnect(timerange);
            DateTime timeOfOrderPlacement = timerange.Item1.AddMinutes(6);

            var bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, timeOfOrderPlacement, timerange.Item2);

            var trailingAmount = 0.2;
            var initialStopPrice = bidAsks.Where(ba => ba.Time == timeOfOrderPlacement).Average(ba => ba.Bid) - trailingAmount;
            var order = new TrailingStopOrder()
            {
                Action = OrderAction.SELL,
                TrailingAmount = trailingAmount,
                TrailingAmountUnits = TrailingAmountUnits.Absolute,
                StopPrice = initialStopPrice,
                TotalQuantity = 50
            };

            _backtester.Account.Positions[Ticker] = new Position(Ticker)
            {
                PositionAmount = 50,
                AverageCost = 413.94,
            };

            _ = _backtester.Start();
            await WaitUntilTimeIsReached(timeOfOrderPlacement);

            // Test
            var placedResult = await _backtester.OrderManager.PlaceOrderAsync(Ticker, order);
            var result = await _backtester.OrderManager.AwaitExecutionAsync(order);

            // Assert
            bidAsks = await _backtester.GetAsync<BidAsk>(Ticker, result.OrderExecution.Time);
            var lowest = bidAsks.Select(ba => ba.Bid).Min();
            var highest = bidAsks.Select(ba => ba.Bid).Max();
            var actualPrice = result.OrderExecution.AvgPrice;
            Assert.LessOrEqual(actualPrice, order.StopPrice.Value);
            Assert.GreaterOrEqual(actualPrice, lowest);
            Assert.LessOrEqual(actualPrice, highest);
        }

        async Task WaitUntilTimeIsReached(DateTime dateTime)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _backtester.ProgressHandler += new Action<BacktesterProgress>(bp =>
            {
                if (bp.Time >= dateTime)
                    tcs.TrySetResult();
            });
            await tcs.Task;
        }
    }
}
