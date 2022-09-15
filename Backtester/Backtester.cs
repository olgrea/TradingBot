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
            // TEST : time scaling
            var day = _historicalData.First();
            var fakeClient = new FakeClient(day.Key.Item1, day.Key.Item2, day.Value, _client, _logger);
            var broker = new IBBroker(123, fakeClient, _logger);
            broker.Connect();
            var contract = broker.GetContract(_ticker);
            broker.RequestBars(contract, BarLength._5Sec);

            Console.ReadKey();

            //foreach(var day in _historicalData)
            //{

            //    //var trader = new Trader(_ticker, broker, _logger);
            //}
        }

        public void Stop()
        {

        }

        public void LoadHistoricalData()
        {
            foreach (var day in DateTimeUtils.GetMarketDays(_startTime, _endTime))
            {
                var barList = BarsUtils.DeserializeBars(Path.Combine(RootDir, BarsUtils.MakeDailyBarsPath(_ticker, day.Item1)));
                _historicalData.Add(day, new LinkedList<Bar>(barList));
            }
        }
    }
}
