using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TradingBot.Broker;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.Client;
using TradingBot.Broker.MarketData;
using TradingBot.Utils;

namespace Backtester
{
    internal class Backtester
    {
        DateTime _start;
        DateTime _end;

        List<(DateTime, DateTime)> _marketDays;

        FakeClient _fakeClient;
        IBClient _client;
        ILogger _logger;
        string _ticker;
        Contract _contract;

        Dictionary<int, PnL> PnLs = new Dictionary<int, PnL>();
        Dictionary<DateTime, LinkedList<Bar>> _historicalData = new Dictionary<DateTime, LinkedList<Bar>>();

        public Backtester(string ticker, DateTime start, DateTime end, ILogger logger)
        {
            _ticker = ticker;
            _logger = logger;
            
            _marketDays = DateTimeUtils.GetMarketDays(start, end).ToList();

            var callbacks = new IBCallbacks(logger);
            _client = new IBClient(callbacks, logger);
            _fakeClient = new FakeClient(_marketDays[0].Item1, _marketDays[0].Item2, _client, logger);
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
        

        public void FetchHistoricalData()
        {
            var marketDays = DateTimeUtils.GetMarketDays(_start, _end);
            foreach (var day in marketDays)
            {
                //var barList = _client.GetHistoricalDataForDayAsync(_contract, day.Item2).Result;
                //_historicalData.Add(day.Item1, barList);
            }
        }
    }
}
