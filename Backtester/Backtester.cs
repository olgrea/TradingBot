using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using MathNet.Numerics.LinearAlgebra.Factorization;
using NLog;
using TradingBot;
using TradingBot.Broker;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Strategies;
using TradingBot.Utils;

[assembly: InternalsVisibleToAttribute("Tests")]
namespace Backtester
{
    internal class Backtester
    {
        const string RootDir = MarketDataUtils.RootDir;

        DateTime _startTime;
        DateTime _endTime;

        ILogger _logger;
        string _ticker;

        Dictionary<int, PnL> _PnLs = new Dictionary<int, PnL>();

        public Backtester(string ticker, DateTime startDate, DateTime endDate)
        {
            _ticker = ticker;

            var marketStartTime = DateTimeUtils.MarketStartTime;
            var marketEndTime = DateTimeUtils.MarketEndTime;
            //_startTime = new DateTime(startDate.Year, startDate.Month, startDate.Day, marketStartTime.Hours, marketStartTime.Minutes, marketStartTime.Seconds);
            //_endTime = new DateTime(endDate.Year, endDate.Month, endDate.Day, marketEndTime.Hours, marketEndTime.Minutes, marketEndTime.Seconds);
            _startTime = new DateTime(startDate.Year, startDate.Month, startDate.Day, 11, 17 ,00);
            _endTime = new DateTime(endDate.Year, endDate.Month, endDate.Day, 11, 47, 00);
            
            _logger = LogManager.GetLogger($"{nameof(Backtester)}"); ;
        }

        public void Start()
        {
            var fakeClient = new FakeClient(_ticker);

            var broker = new IBBroker(1337, fakeClient);
            Trader trader = new Trader(_ticker, broker);
            trader.AddStrategyForTicker<RSIDivergenceStrategy>();

            foreach (var day in DateTimeUtils.GetMarketDays(_startTime, _endTime))
            {
                var marketData = LoadHistoricalData(day.Item1);
                var bars = marketData.Item1;
                var bidAsks = marketData.Item2;

                // For backtesting, we need to have enough past bars to be able to initialize all indicators.
                // So we will set the start time a couple seconds later, corresponding to the highest NbPeriods * BarLength;
                var secondsToAdd = trader.Strategies.Max(s => s.Indicators.Max(i => i.NbPeriods * (int)i.BarLength));
                fakeClient.Init(day.Item1.AddSeconds(secondsToAdd), day.Item2, bars, bidAsks);
                
                try
                {
                    trader.Start();
                    fakeClient.WaitUntilDayIsOver();
                }
                catch (OperationCanceledException)
                {

                }
            }
        }

        public (IEnumerable<Bar>, IEnumerable<BidAsk>) LoadHistoricalData(DateTime date)
        {
            var barList = MarketDataUtils.DeserializeData<Bar>(Path.Combine(RootDir, MarketDataUtils.MakeDailyDataPath<Bar>(_ticker, date)));
            var bidAskList = MarketDataUtils.DeserializeData<BidAsk>(Path.Combine(RootDir, MarketDataUtils.MakeDailyDataPath<BidAsk>(_ticker, date)));
            return (barList, bidAskList);
        }
    }
}
