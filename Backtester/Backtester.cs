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
        Dictionary<(DateTime, DateTime), LinkedList<Bar>> _historicalData = new Dictionary<(DateTime, DateTime), LinkedList<Bar>>();

        public Backtester(string ticker, DateTime startDate, DateTime endDate, ILogger logger)
        {
            _ticker = ticker;

            var marketStartTime = DateTimeUtils.MarketStartTime;
            var marketEndTime = DateTimeUtils.MarketEndTime;
            _startTime = new DateTime(startDate.Year, startDate.Month, startDate.Day, marketStartTime.Hours, marketStartTime.Minutes, marketStartTime.Seconds);
            _endTime = new DateTime(endDate.Year, endDate.Month, endDate.Day, marketEndTime.Hours, marketEndTime.Minutes, marketEndTime.Seconds);
            
            _logger = logger;
            LoadHistoricalData();

            var callbacks = new IBCallbacks(logger);
            _client = new IBClient(callbacks, logger);
        }

        public void Start()
        {

            var fakeClient = new FakeClient(_client, _logger);
            var broker = new IBBroker(1337, fakeClient, _logger);
            Trader trader = new Trader("GME", broker, _logger);
            trader.AddStrategyForTicker<RSIDivergenceStrategy>();
            
            foreach (var day in _historicalData)
            {
                // For backtesting, we need to have enough past bars to be able to initialize all indicators.
                // So we will set the start time a couple seconds later, corresponding to the highest NbPeriods * BarLength;
                var secondsToAdd = trader.Strategies.Max(s => s.Indicators.Max(i => i.NbPeriods * (int)i.BarLength));
                fakeClient.Init(day.Key.Item1.AddSeconds(secondsToAdd), day.Key.Item2, day.Value);
                
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

        public void LoadHistoricalData()
        {
            foreach (var day in DateTimeUtils.GetMarketDays(_startTime, _endTime))
            {
                var barList = MarketDataUtils.DeserializeData<Bar>(Path.Combine(RootDir, MarketDataUtils.MakeDailyDataPath<Bar>(_ticker, day.Item1)));
                _historicalData.Add(day, new LinkedList<Bar>(barList));
            }
        }
    }
}
