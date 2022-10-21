using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TradingBot;
using TradingBot.Broker;
using TradingBot.Broker.Accounts;
using TradingBot.Broker.MarketData;
using TradingBot.Strategies;
using TradingBot.Utils;

[assembly: InternalsVisibleTo("Tests")]
namespace Backtester
{
    internal class Backtester
    {
        const string RootDir = MarketDataUtils.RootDir;

        DateTime _startTime;
        DateTime _endTime;
        string _ticker;

        Dictionary<int, PnL> _PnLs = new Dictionary<int, PnL>();

        public Backtester(string ticker, DateTime startDate, DateTime endDate)
        {
            _ticker = ticker;
            _startTime = new DateTime(startDate.Ticks + MarketDataUtils.MarketStartTime.Ticks, DateTimeKind.Local);
            _endTime = new DateTime(endDate.Ticks + MarketDataUtils.MarketEndTime.Ticks, DateTimeKind.Local);
        }

        public async Task Start()
        {
            foreach (var day in MarketDataUtils.GetMarketDays(_startTime, _endTime))
            {
                var marketData = LoadHistoricalData(_ticker, day.Item1);
                var bars = marketData.Item1;
                var bidAsks = marketData.Item2;

                var fakeClient = new FakeClient(_ticker, day.Item1, day.Item2, bars, bidAsks);
                var broker = new IBBroker(1337, fakeClient);
                Trader trader = new Trader(_ticker, day.Item1, day.Item2, broker, $"{nameof(Backtester)}-{_ticker}_{_startTime.ToShortDateString()}");

                trader.AddStrategyForTicker<RSIDivergenceStrategy>();
                
                await trader.Start();
                trader.Stop();
            }
        }

        static (IEnumerable<Bar>, IEnumerable<BidAsk>) LoadHistoricalData(string symbol, DateTime date)
        {
            var barList = DbUtils.SelectData<Bar>(symbol, date);
            var bidAskList = DbUtils.SelectData<BidAsk>(symbol, date);
            return (barList, bidAskList);
        }

        static (IEnumerable<Bar>, IEnumerable<BidAsk>) DeserializeHistoricalData(string symbol, DateTime date)
        {
            var barList = MarketDataUtils.DeserializeData<Bar>(RootDir, symbol, date);
            var bidAskList = MarketDataUtils.DeserializeData<BidAsk>(RootDir, symbol, date);
            return (barList, bidAskList);
        }
    }
}
