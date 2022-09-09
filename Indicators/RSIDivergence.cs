using System;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    public class RSIDivergence : IIndicator
    {
        RSI _slowRsi;
        RSI _fastRsi;

        public RSIDivergence(int slowPeriod=14, int fastPeriod=5)
        {
            _slowRsi = new RSI(slowPeriod);
            _fastRsi = new RSI(fastPeriod);
        }

        public double Value => _fastRsi.Value - _slowRsi.Value;
        public bool IsReady => _slowRsi.IsReady && _fastRsi.IsReady;
        public int NbPeriods => _slowRsi.NbPeriods;
        public Bar LatestBar => _fastRsi.Bars.Last.Value;

        public void Update(Bar bar)
        {
            _slowRsi.Update(bar);
            _fastRsi.Update(bar);
        }
    }

}
