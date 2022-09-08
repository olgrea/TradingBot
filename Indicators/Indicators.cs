using System.Collections.Generic;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    public class Indicators
    {
        Dictionary<BarLength, List<Bar>> _pastBars = new Dictionary<BarLength, List<Bar>>();
        BarLength _barLength;

        Trader _trader;
        public Indicators(BarLength barLength, Trader trader)
        {
            _barLength = barLength;
            _trader = trader;
        }

        public BollingerBands BollingerBands { get; private set; }
        public BBTrend BBTrend { get; private set; }

        public void Start()
        {
            BBTrend = new BBTrend();
            BollingerBands = new BollingerBands();

            //testInitBollingerBands(_trader.Contract, BarLength._5Sec, BB5Sec);

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
                UpdateIndicator(contract, bar, BollingerBands);
                UpdateIndicator(contract, bar, BBTrend);
            }
        }

        void UpdateIndicator(Contract contract, Bar bar, IIndicator indicator)
        {
            if (!indicator.IsReady)
            {
                var pastBars = GetPastBars(contract, bar, indicator.NbPeriods);
                for(int i = pastBars.Count - indicator.NbPeriods; i < pastBars.Count; ++i)
                    indicator.Update(pastBars[i]);
            }

            indicator.Update(bar);
        }

        // To limit the number of historical data requests
        List<Bar> GetPastBars(Contract contract, Bar bar, int nbPeriods)
        {
            if (!_pastBars.ContainsKey(bar.BarLength))
                _pastBars.Add(bar.BarLength, new List<Bar>());

            if (_pastBars[bar.BarLength].Count < nbPeriods)
            {
                _pastBars[bar.BarLength] = _trader.Broker.GetPastBars(contract, bar.Time, bar.BarLength, nbPeriods);
            }

            return _pastBars[bar.BarLength];
        }

        //void testInitBollingerBands(Contract contract, BarLength barLength, BollingerBands bb)
        //{
        //    if (!bb.IsReady)
        //    {
        //        //_trader.Broker.GetPastBars(contract, DateTime.Now, bar.BarLength, BollingerBands.NbPeriods);

        //        //TEST
        //        string format = "yyyyMMdd-HH:mm:ss";
        //        var pastBars = _trader.Broker.GetPastBars(contract, DateTime.ParseExact("20220907-16:00:00", format, CultureInfo.InvariantCulture), barLength, BollingerBands.NbPeriods);
        //        foreach (var pastBar in pastBars)
        //            bb.Update(pastBar);
        //    }
        //}
    }
}
