using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        const int TimeScale = 1;

        const int DefaultPort = 7496;
        const string DefaultIP = "127.0.0.1";
        int _clientId = 9000;

        string _ticker;
        DateTime _start;
        DateTime _end;

        TWSClient _client;
        ILogger _logger;
        Contract _contract;

        Dictionary<DateTime, LinkedList<Bar>> _historicalData = new Dictionary<DateTime, LinkedList<Bar>>();
        Dictionary<int, PnL> PnLs = new Dictionary<int, PnL>();

        public Backtester(string ticker, DateTime from, DateTime to, ILogger logger)
        {
            _ticker = ticker;
            _start = from;
            _end = to;
            _logger = logger;
            _client = new TWSClient(logger);
            var _trader = new Trader(ticker, new FakeBroker(logger), logger);
        }

        public void Start()
        {
            _client.ConnectAsync(DefaultIP, DefaultPort, _clientId).Wait();
            
            var contractList = _client.GetContractsAsync(_ticker).Result;
            _contract = contractList.First();

            FetchHistoricalData();

        }

        public void Stop()
        {

        }

        bool IsWeekend(DateTime dt) => dt.DayOfWeek == DayOfWeek.Sunday || dt.DayOfWeek == DayOfWeek.Saturday;

        public IEnumerable<(DateTime, DateTime)> GetMarketDays(DateTime start, DateTime end)
        {
            if (end <= start)
                yield break;

            DateTime marketStartTime = new DateTime(start.Year, start.Month, start.Day, 9, 30, 0);
            DateTime marketEndTime = new DateTime(start.Year, start.Month, start.Day, 16, 0, 0); 

            if(start < marketStartTime)
                start = marketStartTime;

            int i = 0;
            DateTime current = start;
            while (current < end)
            {
                if(!IsWeekend(current))
                {
                    if(i==0 && start < marketEndTime)
                        yield return (start, marketEndTime);
                    else if(i > 0)
                        yield return (marketStartTime, marketEndTime);
                }

                current = current.AddDays(1);
                marketStartTime = marketStartTime.AddDays(1);
                marketEndTime = marketEndTime.AddDays(1);
                i++;
            }

            if (!IsWeekend(end) && end > marketStartTime)
            {
                if (end > marketEndTime)
                    end = marketEndTime;

                yield return (marketStartTime, end);
            }
        }
        
        public void FetchHistoricalData()
        {
            var marketDays = GetMarketDays(_start, _end);
            foreach (var day in marketDays)
            {
                var barList = _client.GetHistoricalDataForDayAsync(_contract, day.Item2).Result;

                _historicalData.Add(day.Item1, barList);
            }
        }
    }
}
