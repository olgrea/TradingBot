using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using NLog;
using TradingBot;
using TradingBot.Broker;
using TradingBot.Broker.Accounts;
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

        Contract _contract;

        Dictionary<int, PnL> _PnLs = new Dictionary<int, PnL>();

        public Backtester(string ticker, DateTime startDate, DateTime endDate)
        {
            _startTime = new DateTime(startDate.Ticks + DateTimeUtils.MarketStartTime.Ticks, DateTimeKind.Local);
            _endTime = new DateTime(endDate.Ticks + DateTimeUtils.MarketEndTime.Ticks, DateTimeKind.Local);
            
            var broker = new IBBroker();
            broker.Connect();
            _contract = broker.GetContract(ticker);
            broker.Disconnect();

            FakeClient.TimeDelays.TimeScale = 0.001;
        }

        public void Start()
        {
            foreach (var day in DateTimeUtils.GetMarketDays(_startTime, _endTime))
            {
                var marketData = LoadHistoricalData(day.Item1);
                var bars = marketData.Item1;
                var bidAsks = marketData.Item2;

                var fakeClient = new FakeClient(_contract, day.Item1, day.Item2, bars, bidAsks);
                var broker = new IBBroker(1337, fakeClient);
                Trader trader = new Trader(_contract.Symbol, day.Item1, day.Item2, broker, $"{nameof(Backtester)}-{_contract.Symbol}_{_startTime.ToShortDateString()}");
                trader.AddStrategyForTicker<TestStrategy>();
                
                trader.Start().Wait();
                trader.Stop();
            }
        }

        public (IEnumerable<Bar>, IEnumerable<BidAsk>) LoadHistoricalData(DateTime date)
        {
            var barList = MarketDataUtils.DeserializeData<Bar>(Path.Combine(RootDir, MarketDataUtils.MakeDailyDataPath<Bar>(_contract.Symbol, date)));
            var bidAskList = MarketDataUtils.DeserializeData<BidAsk>(Path.Combine(RootDir, MarketDataUtils.MakeDailyDataPath<BidAsk>(_contract.Symbol, date)));
            return (barList, bidAskList);
        }
    }
}
