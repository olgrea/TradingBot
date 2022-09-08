using System;
using System.Globalization;
using TradingBot.Broker;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    public class Indicators
    {
        Trader _trader;
        public Indicators(Trader trader)
        {
            _trader = trader;
        }

        public BollingerBands BB1Min { get; private set; }
        public BollingerBands BB5Sec { get; private set; }

        public void Start()
        {
            BB1Min = new BollingerBands();
            BB5Sec = new BollingerBands();

            //testInitBollingerBands(_trader.Contract, BarLength._5Sec, BB5Sec);
            //testInitBollingerBands(_trader.Contract, BarLength._1Min, BB1Min);

            _trader.Broker.Bar5SecReceived += OnBarsReceived;
            _trader.Broker.Bar1MinReceived += OnBarsReceived;
            _trader.Broker.RequestBars(_trader.Contract, Broker.MarketData.BarLength._5Sec);
            _trader.Broker.RequestBars(_trader.Contract, Broker.MarketData.BarLength._1Min);
        }

        public void Stop()
        {
            _trader.Broker.Bar5SecReceived -= OnBarsReceived;
            _trader.Broker.Bar1MinReceived -= OnBarsReceived;
            _trader.Broker.CancelBarsRequest(_trader.Contract, Broker.MarketData.BarLength._5Sec);
            _trader.Broker.CancelBarsRequest(_trader.Contract, Broker.MarketData.BarLength._1Min);

            BB1Min = null;
            BB5Sec = null;
        }

        void OnBarsReceived(Contract contract, Bar bar)
        {
            if (bar.BarLength == BarLength._5Sec)
            {
                UpdateBollingerBands(contract, bar, BB5Sec);
            }
            else if (bar.BarLength == BarLength._1Min)
            {
                UpdateBollingerBands(contract, bar, BB1Min);
            }
        }

        void UpdateBollingerBands(Contract contract, Bar bar, BollingerBands bb)
        {
            if (!bb.IsReady)
            {
                var pastBars = _trader.Broker.GetPastBars(contract, bar.Time, bar.BarLength, BollingerBands.NbPeriods);
                foreach(var pastBar in pastBars)
                    bb.Update(pastBar);
            }

            bb.Update(bar);
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
