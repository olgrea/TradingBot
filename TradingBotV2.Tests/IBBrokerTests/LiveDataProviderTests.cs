using NUnit.Framework;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.IBKR;

namespace IBBrokerTests
{
    [TestFixture]
    public class LiveDataProviderTests
    {
        IBBroker _broker;

        [SetUp]
        public async Task OneTimeSetUp()
        {
            _broker = new IBBroker(9001);
            await _broker.ConnectAsync();
        }

        [TearDown]
        public async Task OneTimeTearDown()
        {
            await Task.Delay(50);
            await _broker.DisconnectAsync();
            await Task.Delay(50);
        }

        [Test]
        public async Task RequestBidAskUpdates_SingleTickerSubscription()
        {
            if (!MarketDataUtils.IsMarketOpen())
                Assert.Ignore("Market is not open.");

            string expectedTicker = "SPY";
            var baList = new List<BidAsk>();

            var tcs = new TaskCompletionSource<bool>();
            var bidAskReceived = new Action<string, BidAsk>((ticker, bidAsk) =>
            {
                if (expectedTicker == ticker)
                {
                    baList.Add(bidAsk);
                    if (baList.Count == 3)
                        tcs.TrySetResult(true);
                }
            });

            _broker.LiveDataProvider.BidAskReceived += bidAskReceived;
            try
            {
                _broker.LiveDataProvider.RequestBidAskUpdates(expectedTicker);
                await tcs.Task;
            }
            finally
            {
                _broker.LiveDataProvider.CancelBidAskUpdates(expectedTicker);
                _broker.LiveDataProvider.BidAskReceived -= bidAskReceived;
            }

            Assert.IsNotEmpty(baList);
            Assert.IsTrue(baList.Count == 3);
        }

        [Test]
        public async Task RequestLastUpdates_SingleTickerSubscription()
        {
            if (!MarketDataUtils.IsMarketOpen())
                Assert.Ignore("Market is not open.");

            string expectedTicker = "SPY";
            var lastList = new List<Last>();

            var tcs = new TaskCompletionSource<bool>();
            var lastReceived = new Action<string, Last>((ticker, last) =>
            {
                if (expectedTicker == ticker)
                {
                    lastList.Add(last);
                    if (lastList.Count == 3)
                        tcs.TrySetResult(true);
                }
            });

            _broker.LiveDataProvider.LastReceived += lastReceived;
            try
            {
                _broker.LiveDataProvider.RequestLastTradedPriceUpdates(expectedTicker);
                await tcs.Task;
            }
            finally
            {
                _broker.LiveDataProvider.CancelLastTradedPriceUpdates(expectedTicker);
                _broker.LiveDataProvider.LastReceived -= lastReceived;
            }

            Assert.IsNotEmpty(lastList);
            Assert.IsTrue(lastList.Count == 3);
        }


        // No more than 1 tick-by-tick request can be made for the same instrument within 15 seconds.
        // https://interactivebrokers.github.io/tws-api/tick_data.html
        [Test]
        [Ignore("It seems to be working fine. Not sure why the doc is saying that.")]
        public void RequestTickByTickData_SameTicker_Under15seconds_ShouldFail()
        {
            if (!MarketDataUtils.IsMarketOpen())
                Assert.Ignore("Market is not open.");

            string expectedTicker = "SPY";

            try
            {
                _broker.LiveDataProvider.RequestBidAskUpdates(expectedTicker);
                //_broker.MarketDataProvider.RequestLastTradedPriceUpdates(expectedTicker);
                Assert.Throws<ErrorMessage>(() => _broker.LiveDataProvider.RequestLastTradedPriceUpdates(expectedTicker));
            }
            finally
            {
                _broker.LiveDataProvider.CancelBidAskUpdates(expectedTicker);
                _broker.LiveDataProvider.CancelLastTradedPriceUpdates(expectedTicker);
            }
        }

        // The maximum number of simultaneous tick-by-tick subscriptions allowed for a user is determined by the same formula
        // used to calculate maximum number of market depth subscriptions Limitations.
        // For 100 market data lines : max request = 3
        // https://interactivebrokers.github.io/tws-api/tick_data.html
        // https://interactivebrokers.github.io/tws-api/market_depth.html#limitations
        // https://interactivebrokers.github.io/tws-api/market_data.html#market_lines
        [Test]
        [Ignore("It seems to be working fine. Not sure why the doc is saying that.")]
        public void RequestTickByTickData_NbOfRequestsOver3_ShouldFail()
        {
            if (!MarketDataUtils.IsMarketOpen())
                Assert.Ignore("Market is not open.");

            string[] tickers = { "SPY", "QQQ", "GME", "AMC" };
            try
            {
                for (int i = 0; i < tickers.Length; i++)
                {
                    if (i < tickers.Length - 1)
                        _broker.LiveDataProvider.RequestBidAskUpdates(tickers[i]);
                    else
                        Assert.Throws<ErrorMessage>(() => _broker.LiveDataProvider.RequestBidAskUpdates(tickers[i]));
                }
            }
            finally
            {
                foreach (string ticker in tickers)
                {
                    _broker.LiveDataProvider.CancelBidAskUpdates(ticker);
                }
            }
        }

        [Test]
        public async Task RequestBarUpdates_SingleTickerSubscription_SingleBarLength()
        {
            if (!MarketDataUtils.IsMarketOpen())
                Assert.Ignore("Market is not open.");

            string expectedTicker = "SPY";
            var barList = new List<Bar>();

            var tcs = new TaskCompletionSource<bool>();
            var barReceived = new Action<string, Bar>((ticker, bar) =>
            {
                if (expectedTicker == ticker && bar.BarLength == BarLength._5Sec)
                {
                    barList.Add(bar);
                    if (barList.Count == 3)
                        tcs.TrySetResult(true);
                }
            });

            _broker.LiveDataProvider.BarReceived += barReceived;
            try
            {
                _broker.LiveDataProvider.RequestBarUpdates(expectedTicker, BarLength._5Sec);
                await tcs.Task;
            }
            finally
            {
                _broker.LiveDataProvider.CancelBarUpdates(expectedTicker, BarLength._5Sec);
                _broker.LiveDataProvider.BarReceived -= barReceived;
            }

            Assert.IsNotEmpty(barList);
            Assert.AreEqual(3, barList.Count);
            foreach(Bar bar in barList)
            {
                Assert.AreEqual(0, bar.Time.Second % 5);
            }
        }

        [Test]
        public async Task RequestBarUpdates_SingleTickerSubscription_MultipleBarLengths()
        {
            if (!MarketDataUtils.IsMarketOpen())
                Assert.Ignore("Market is not open.");

            string expectedTicker = "SPY";
            var fiveSecBars = new List<Bar>();
            var oneMinBars = new List<Bar>();

            var tcs = new TaskCompletionSource<bool>();
            var barReceived = new Action<string, Bar>((ticker, bar) =>
            {
                if (expectedTicker == ticker)
                {
                    if(bar.BarLength == BarLength._5Sec)
                    {
                        fiveSecBars.Add(bar);
                    }

                    if (bar.BarLength == BarLength._1Min)
                    {
                        oneMinBars.Add(bar);
                        if (oneMinBars.Count == 1)
                            tcs.TrySetResult(true);
                    }
                }
            });

            _broker.LiveDataProvider.BarReceived += barReceived;
            try
            {
                _broker.LiveDataProvider.RequestBarUpdates(expectedTicker, BarLength._5Sec);
                _broker.LiveDataProvider.RequestBarUpdates(expectedTicker, BarLength._1Min);
                await tcs.Task;
            }
            finally
            {
                _broker.LiveDataProvider.CancelBarUpdates(expectedTicker, BarLength._5Sec);
                _broker.LiveDataProvider.CancelBarUpdates(expectedTicker, BarLength._1Min);
                _broker.LiveDataProvider.BarReceived -= barReceived;
            }

            Assert.IsNotEmpty(fiveSecBars);
            Assert.GreaterOrEqual(fiveSecBars.Count, 60 / 5);
            foreach (Bar bar in fiveSecBars)
            {
                Assert.AreEqual(0, bar.Time.Second % 5);
            }

            Assert.IsNotEmpty(oneMinBars);
            Assert.AreEqual(1, oneMinBars.Count);
            Assert.AreEqual(0, oneMinBars.First().Time.Second % 60);
        }

        [Test]
        public async Task RequestBarUpdates_MultipleTickerSubscriptions_SingleBarLengths()
        {
            if (!MarketDataUtils.IsMarketOpen())
                Assert.Ignore("Market is not open.");

            string[] expectedTickers = { "SPY", "QQQ" };
            List<Bar>[] fiveSecBars = { new List<Bar>(), new List<Bar>() };

            var tcs = new TaskCompletionSource<bool>();
            var barReceived = new Action<string, Bar>((ticker, bar) =>
            {
                if (expectedTickers[0] == ticker)
                {
                    if (bar.BarLength == BarLength._5Sec && fiveSecBars[0].Count < 3)
                    {
                        fiveSecBars[0].Add(bar);
                    }
                }

                if (expectedTickers[1] == ticker)
                {
                    if (bar.BarLength == BarLength._5Sec && fiveSecBars[1].Count < 3)
                    {
                        fiveSecBars[1].Add(bar);
                    }
                }

                if (fiveSecBars[0].Count == 3 && fiveSecBars[1].Count == 3)
                    tcs.TrySetResult(true);
            });

            _broker.LiveDataProvider.BarReceived += barReceived;
            try
            {
                _broker.LiveDataProvider.RequestBarUpdates(expectedTickers[0], BarLength._5Sec);
                _broker.LiveDataProvider.RequestBarUpdates(expectedTickers[1], BarLength._5Sec);
                await tcs.Task;
            }
            finally
            {
                _broker.LiveDataProvider.CancelBarUpdates(expectedTickers[0], BarLength._5Sec);
                _broker.LiveDataProvider.CancelBarUpdates(expectedTickers[1], BarLength._5Sec);
                _broker.LiveDataProvider.BarReceived -= barReceived;
            }

            foreach(var bars in fiveSecBars)
            {
                Assert.IsNotEmpty(bars);
                Assert.AreEqual(3, bars.Count);
                foreach (Bar bar in bars)
                {
                    Assert.AreEqual(0, bar.Time.Second % 5);
                }
            }
        }

        [Test]
        public async Task RequestBarUpdates_MultipleTickerSubscriptions_MultipleBarLengths()
        {
            if (!MarketDataUtils.IsMarketOpen())
                Assert.Ignore("Market is not open.");

            string[] expectedTickers = { "SPY", "QQQ" };
            List<Bar>[] fiveSecBars = { new List<Bar>(), new List<Bar>() };
            List<Bar>[] oneMinuteBars = { new List<Bar>(), new List<Bar>() };

            var tcs = new TaskCompletionSource<bool>();
            var barReceived = new Action<string, Bar>((ticker, bar) =>
            {
                if (expectedTickers[0] == ticker)
                {
                    if (bar.BarLength == BarLength._5Sec && fiveSecBars[0].Count < 3)
                    {
                        fiveSecBars[0].Add(bar);
                    }

                    if (bar.BarLength == BarLength._1Min && oneMinuteBars[0].Count == 0)
                    {
                        oneMinuteBars[0].Add(bar);
                    }
                }

                if (expectedTickers[1] == ticker)
                {
                    if (bar.BarLength == BarLength._5Sec && fiveSecBars[1].Count < 3)
                    {
                        fiveSecBars[1].Add(bar);
                    }

                    if (bar.BarLength == BarLength._1Min && oneMinuteBars[1].Count == 0)
                    {
                        oneMinuteBars[1].Add(bar);
                    }
                }

                if (oneMinuteBars[0].Count == 1 && oneMinuteBars[1].Count == 1)
                    tcs.TrySetResult(true);
            });

            _broker.LiveDataProvider.BarReceived += barReceived;
            try
            {
                _broker.LiveDataProvider.RequestBarUpdates(expectedTickers[0], BarLength._5Sec);
                _broker.LiveDataProvider.RequestBarUpdates(expectedTickers[0], BarLength._1Min);
                _broker.LiveDataProvider.RequestBarUpdates(expectedTickers[1], BarLength._5Sec);
                _broker.LiveDataProvider.RequestBarUpdates(expectedTickers[1], BarLength._1Min);
                await tcs.Task;
            }
            finally
            {
                _broker.LiveDataProvider.CancelBarUpdates(expectedTickers[0], BarLength._5Sec);
                _broker.LiveDataProvider.CancelBarUpdates(expectedTickers[0], BarLength._1Min);
                _broker.LiveDataProvider.CancelBarUpdates(expectedTickers[1], BarLength._5Sec);
                _broker.LiveDataProvider.CancelBarUpdates(expectedTickers[1], BarLength._1Min);
                _broker.LiveDataProvider.BarReceived -= barReceived;
            }

            foreach (var bars in fiveSecBars)
            {
                Assert.IsNotEmpty(bars);
                Assert.GreaterOrEqual(60/5, bars.Count);
                foreach (Bar bar in bars)
                {
                    Assert.AreEqual(0, bar.Time.Second % 5);
                }
            }

            foreach (var bars in oneMinuteBars)
            {
                Assert.IsNotEmpty(bars);
                Assert.AreEqual(1, bars.Count);
                Assert.AreEqual(0, bars[0].Time.Second % 60);
            }
        }
    }
}
