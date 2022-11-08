using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DataStorage.Db.DbCommandFactories;
using InteractiveBrokers;
using InteractiveBrokers.Accounts;
using InteractiveBrokers.MarketData;
using TradingBot;
using TradingBot.Strategies;

[assembly: InternalsVisibleTo("Backtester.Tests")]
namespace Backtester
{
    internal class Backtester
    {
        DateTime _startTime;
        DateTime _endTime;
        string _ticker;
        BarCommandFactory _barCommandFactory;
        BidAskCommandFactory _bidAskCommandFactory;

        Dictionary<int, PnL> _PnLs = new Dictionary<int, PnL>();

        public Backtester(string ticker, DateTime startDate, DateTime endDate)
        {
            _ticker = ticker;
            _startTime = new DateTime(startDate.Ticks + Utils.MarketStartTime.Ticks, DateTimeKind.Local);
            _endTime = new DateTime(endDate.Ticks + Utils.MarketEndTime.Ticks, DateTimeKind.Local);
            _barCommandFactory = new BarCommandFactory(BarLength._1Sec);
            _bidAskCommandFactory = new BidAskCommandFactory();
        }

        public async Task Start()
        {

            foreach (var day in Utils.GetMarketDays(_startTime, _endTime))
            {
                var marketData = LoadHistoricalData(_ticker, day.Item1.Date, (day.Item1.TimeOfDay, day.Item2.TimeOfDay));
                var bars = marketData.Item1;
                var bidAsks = marketData.Item2;

                var fakeClient = new FakeIBSocket(_ticker, day.Item1, day.Item2, bars, bidAsks);
                var client = new IBClient(1337, fakeClient);
                Trader trader = new Trader(_ticker, day.Item1, day.Item2, client, $"{nameof(Backtester)}-{_ticker}_{_startTime.ToShortDateString()}");

                trader.AddStrategyForTicker<RSIDivergenceStrategy>();
                
                await trader.Start();
                trader.Stop();
            }
        }

        (IEnumerable<Bar>, IEnumerable<BidAsk>) LoadHistoricalData(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange)
        {
            var barSelectCmd = _barCommandFactory.CreateSelectCommand(symbol, date, timeRange);
            var bidAskSelectCmd = _bidAskCommandFactory.CreateSelectCommand(symbol, date, timeRange);

            var barList = barSelectCmd.Execute();
            var bidAskList = bidAskSelectCmd.Execute();
            return (barList, bidAskList);
        }
    }
}
