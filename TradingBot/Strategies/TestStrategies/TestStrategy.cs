using System;
using System.Collections.Generic;
using System.Text;
using MarketDataUtils = InteractiveBrokers.MarketData.Utils;

namespace TradingBot.Strategies
{
    public class TestStrategy : IStrategy
    {
        public TestStrategy(Trader trader)
        {
            StartTime = MarketDataUtils.MarketStartTime;
            EndTime = MarketDataUtils.MarketEndTime;
            IndicatorStrategy = new BollingerBandsStrategy();
            OrderStrategy = new NaiveStrategy(trader);
        }

        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public IIndicatorStrategy IndicatorStrategy { get; set; }
        public IOrderStrategy OrderStrategy { get; set; }
    }
}
