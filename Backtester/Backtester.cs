using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MathNet.Numerics.LinearAlgebra.Factorization;
using TradingBot;
using TradingBot.Broker;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Strategies;
using TradingBot.Utils;

namespace Backtester
{
    internal class Backtester
    {
        const string RootDir = @"D:\dev\repos\TradingBot\HistoricalDataFetcher\bin\Debug\netcoreapp3.1\historical";

        DateTime _startTime;
        DateTime _endTime;

        IBClient _client;
        ILogger _logger;
        string _ticker;

        Dictionary<int, PnL> _PnLs = new Dictionary<int, PnL>();

        public Backtester(string ticker, DateTime startDate, DateTime endDate, ILogger logger)
        {
            _ticker = ticker;

            var marketStartTime = DateTimeUtils.MarketStartTime;
            var marketEndTime = DateTimeUtils.MarketEndTime;
            _startTime = new DateTime(startDate.Year, startDate.Month, startDate.Day, marketStartTime.Hours, marketStartTime.Minutes, marketStartTime.Seconds);
            _endTime = new DateTime(endDate.Year, endDate.Month, endDate.Day, marketEndTime.Hours, marketEndTime.Minutes, marketEndTime.Seconds);
            
            _logger = logger;

            var callbacks = new IBCallbacks(logger);
            _client = new IBClient(callbacks, logger);
        }

        public void Start()
        {

            var fakeClient = new FakeClient(_client, _logger);
            var broker = new IBBroker(1337, fakeClient, _logger);
            Trader trader = new Trader(_ticker, broker, _logger);
            trader.AddStrategyForTicker<RSIDivergenceStrategy>();
            
            var contract = broker.GetContract(_ticker);

            foreach (var day in DateTimeUtils.GetMarketDays(_startTime, _endTime))
            {
                var marketData = LoadHistoricalData(day.Item1);
                var bars = marketData.Item1;
                var bidAsks = marketData.Item2;

                // For backtesting, we need to have enough past bars to be able to initialize all indicators.
                // So we will set the start time a couple seconds later, corresponding to the highest NbPeriods * BarLength;
                var secondsToAdd = trader.Strategies.Max(s => s.Indicators.Max(i => i.NbPeriods * (int)i.BarLength));
                fakeClient.Init(contract, day.Item1.AddSeconds(secondsToAdd), day.Item2, bars, bidAsks);
                
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
