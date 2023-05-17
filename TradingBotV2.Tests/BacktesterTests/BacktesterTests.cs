using NUnit.Framework;
using TradingBotV2.Backtesting;
using TradingBotV2.Tests;
using TradingBotV2.Utils;

namespace BacktesterTests
{
    internal class BacktesterTests
    {
        DateOnly _openDay;
        Backtester _backtester;
        Action<BacktesterProgress>? _progressHandler;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            _openDay = MarketDataUtils.FindLastOpenDay(DateTime.Now.AddDays(-1));

            var from = _openDay.ToDateTime(TimeOnly.FromTimeSpan(MarketDataUtils.MarketStartTime));
            _backtester = TestsUtils.CreateBacktester(from, from.AddHours(1));
            //_backtester.TimeCompression.Factor = 0;

            await _backtester.ConnectAsync();
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDown()
        {
            await _backtester.DisconnectAsync();
            await _backtester.DisposeAsync();
        }

        [TearDown]
        public void TearDown()
        {
            _backtester.ProgressHandler -= _progressHandler;
            _backtester.Stop();
            _backtester.Reset();
        }

        [Test]
        public async Task Backtester_NotConnected_WhenStarted_Throws()
        {
            Backtester bt = null;
            try
            {
                bt = TestsUtils.CreateBacktester(_openDay);
                Assert.ThrowsAsync<InvalidOperationException>(async () => await bt.Start());
            }
            finally
            {
                await bt.DisposeAsync();
            }
        }

        [Test]
        public void Backtester_CurrentDay_WhenCreated_Throws()
        {
            Assert.Throws<ArgumentException>(() => TestsUtils.CreateBacktester(DateOnly.FromDateTime(DateTime.Now)));
        }

        [Test]
        public async Task Backtester_WhenStarted_UpdateProgress()
        {
            BacktesterProgress? firstProgress = null;
            BacktesterProgress? lastProgress = null;
            _progressHandler = new Action<BacktesterProgress>(p => 
            {
                if (firstProgress == null)
                    firstProgress = p;
                else
                    lastProgress = p;
            });

            _backtester.ProgressHandler += _progressHandler;
            _ = _backtester.Start();
            await Task.Delay(1000);
            _backtester.Stop();
            Assert.NotNull(firstProgress);
            Assert.NotNull(lastProgress);
            Assert.AreEqual(_backtester.StartTime, firstProgress.Value.Time);
            Assert.AreEqual(_backtester.LastProcessedTime, lastProgress.Value.Time);
        }

        [Test]
        public async Task Backtester_WhenCompleted_ReturnsResults()
        {
            var result = await _backtester.Start();
            Assert.NotNull(result);
            Assert.AreEqual(_backtester.StartTime, result.Start);
            Assert.AreEqual(_backtester.EndTime, result.End);
            Assert.Greater(result.RunTime, TimeSpan.FromSeconds(0));
        }

        [Test]
        public async Task Backtester_CanBeStopped()
        {
            var task = _backtester.Start();
            await Task.Delay(2000);
            _backtester.Stop();
            var currentTime = _backtester.CurrentTime;
            await Task.Delay(1000);
            Assert.AreEqual(currentTime, _backtester.CurrentTime);  
        }

        [Test]
        public async Task Backtester_WhenStopped_CanBeResumed()
        {
            BacktesterProgress? progress = null;
            BacktesterProgress? progressOnStop = null;
            BacktesterProgress? progressOnResumed = null;
            _progressHandler = new Action<BacktesterProgress>(p =>
            {
                progress = p;
                if (progressOnStop != null && progressOnResumed == null)
                    progressOnResumed = progress;
            });

            _backtester.ProgressHandler += _progressHandler;
            _ = _backtester.Start();

            await Task.Delay(1000);
            _backtester.Stop();
            progressOnStop = progress;
            Assert.NotNull(progress);
            Assert.Greater(progressOnStop.Value.Time, _backtester.StartTime);

            var result = await _backtester.Start();
            Assert.NotNull(progressOnResumed);
            Assert.AreEqual(progressOnResumed.Value.Time, progressOnStop.Value.Time.AddSeconds(1));
            Assert.AreEqual(_backtester.LastProcessedTime, progress.Value.Time);
        }

        [Test]
        public async Task Backtester_WhenStopped_CanBeReset()
        {
            BacktesterProgress? progress = null;
            _progressHandler = new Action<BacktesterProgress>(p =>
            {
                progress = p;
            });

            _backtester.ProgressHandler += _progressHandler;
            _ = _backtester.Start();
            await Task.Delay(1000);
            _backtester.Stop();
            BacktesterProgress? progressOnStop = progress;
            Assert.Greater(progressOnStop.Value.Time, _backtester.StartTime);
            _backtester.Reset();
            Assert.AreEqual(_backtester.StartTime, _backtester.CurrentTime);
        }

        [Test]
        public async Task Backtester_WhenReset_CanBeStartedAgain()
        {
            BacktesterProgress? progress = null;
            _progressHandler = new Action<BacktesterProgress>(p =>
            {
                progress = p;
            });

            _backtester.ProgressHandler += _progressHandler;
            _ = _backtester.Start();
            await Task.Delay(1000);
            _backtester.Stop();
            BacktesterProgress? progressOnStop = progress;
            Assert.Greater(progressOnStop.Value.Time, _backtester.StartTime);

            await Task.Delay(1000);
            _backtester.Reset();
            Assert.AreEqual(_backtester.StartTime, _backtester.CurrentTime);

            await _backtester.Start();
            Assert.AreEqual(_backtester.LastProcessedTime, progress.Value.Time);
            Assert.AreEqual(_backtester.EndTime, _backtester.CurrentTime);
        }

        [Test]
        public async Task Backtester_CanChangeBacktestingSpeed()
        {
            var from = _openDay.ToDateTime(TimeOnly.FromTimeSpan(MarketDataUtils.MarketStartTime));
            var minutes = 10;
            var to = from.AddMinutes(minutes);

            Backtester backtester = null;
            try
            {
                backtester = TestsUtils.CreateBacktester(from, to);
                await backtester.ConnectAsync();
                backtester.TimeCompression.Factor = 0.0001;
                var res1 = await backtester.Start();

                backtester.TimeCompression.Factor = 0.0005;
                backtester.Reset();
                var res2 = await backtester.Start();

                Assert.Greater(res2.RunTime, res1.RunTime);
            }
            finally
            {
                await backtester.DisconnectAsync();
                await backtester.DisposeAsync();
            }
        }
    }
}
