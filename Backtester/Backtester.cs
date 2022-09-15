using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MathNet.Numerics.LinearAlgebra.Factorization;
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

        List<(DateTime, DateTime)> _marketDays;

        FakeClient _fakeClient;
        IBClient _client;
        ILogger _logger;
        string _ticker;
        Contract _contract;

        Dictionary<int, PnL> PnLs = new Dictionary<int, PnL>();
        Dictionary<DateTime, LinkedList<Bar>> _historicalData = new Dictionary<DateTime, LinkedList<Bar>>();

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

            //_fakeClient = new FakeClient(_marketDays[0].Item1, _marketDays[0].Item2, _client, logger);
        }

        public void Start()
        {
            foreach(var day in _marketDays)
            {

            }
        }

        public void Stop()
        {

        }

        public void LoadHistoricalData()
        {
            _marketDays = DateTimeUtils.GetMarketDays(_startTime, _endTime).ToList();
            foreach (var day in _marketDays)
            {
                var barList = BarsUtils.DeserializeBars(Path.Combine(RootDir, BarsUtils.MakeDailyBarsPath(_ticker, day.Item1)));
                _historicalData.Add(day.Item1, new LinkedList<Bar>(barList));
            }
        }
    }
}
