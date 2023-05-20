using Skender.Stock.Indicators;
using TradingBotV2.Broker.MarketData;

namespace TradingBotV2.Indicators
{
    internal enum BollingerBandsSignals
    {
        None = 0,
        Overbought,
        Oversold,
        OverSma, 
        UnderSma,
    }

    internal class BollingerBands : IndicatorBase<BollingerBandsResult, BollingerBandsSignals>
    {
        public BollingerBands(BarLength barLength, int nbPeriods = 20) : base(barLength, nbPeriods, nbPeriods)
        {
        }

        protected override IEnumerable<BollingerBandsResult> ComputeResults()
        {
            return _quotes!.Use(CandlePart.Close).GetBollingerBands();
        }

        protected override IEnumerable<BollingerBandsSignals> ComputeSignals()
        {
            List<BollingerBandsSignals> signals = new List<BollingerBandsSignals>();
            if (IsReady && _results.Any() && _quotes.Any())
            {
                var latestVal = Convert.ToDouble(_quotes.Last().Close);
                var latestResult = _results.Last();

                if (latestVal > latestResult.Sma)
                    signals.Add(BollingerBandsSignals.OverSma);
                else
                    signals.Add(BollingerBandsSignals.UnderSma);

                if(latestVal > latestResult.UpperBand)
                    signals.Add(BollingerBandsSignals.Overbought);
                else if(latestVal < latestResult.LowerBand)
                    signals.Add(BollingerBandsSignals.Oversold);
            }

            return signals;
        }
    }
}
