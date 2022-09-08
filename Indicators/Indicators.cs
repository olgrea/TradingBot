using System.Collections.Generic;
using System.Linq;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    public class Indicators
    {
        BarLength _barLength;

        Trader _trader;
        public Indicators(BarLength barLength, Trader trader)
        {
            _barLength = barLength;
            _trader = trader;
        }

        public MovingAverage MovingAverage => BollingerBands.MovingAverage;
        public BollingerBands BollingerBands { get; private set; }
        public BBTrend BBTrend { get; private set; }
        public Bar LatestBar => BollingerBands.Bars.LastOrDefault();

        public void Start()
        {
            BBTrend = new BBTrend();
            BollingerBands = new BollingerBands();

            InitIndicators(BBTrend);
            InitIndicators(BollingerBands);

            _trader.Broker.BarReceived[_barLength] += OnBarsReceived;
            _trader.Broker.RequestBars(_trader.Contract, _barLength);
        }

        public void Stop()
        {
            _trader.Broker.BarReceived[_barLength] -= OnBarsReceived;
            _trader.Broker.CancelBarsRequest(_trader.Contract, _barLength);

            BollingerBands = null;
            BBTrend = null;
        }

        void OnBarsReceived(Contract contract, Bar bar)
        {
            if (bar.BarLength == _barLength)
            {
                UpdateIndicator(contract, bar, BBTrend);
                UpdateIndicator(contract, bar, BollingerBands);
            }
        }

        void UpdateIndicator(Contract contract, Bar bar, IIndicator indicator)
        {
            indicator.Update(bar);
        }

        void InitIndicators(params IIndicator[] indicators)
        {
            var longestPeriod = indicators.Max(i => i.NbPeriods);

            var pastBars = _trader.Broker.GetPastBars(_trader.Contract, _barLength, longestPeriod);

            foreach (var indicator in indicators)
            {
                for (int i = pastBars.Count - indicator.NbPeriods; i < pastBars.Count; ++i)
                    indicator.Update(pastBars[i]);
            }
        }
    }
}
