using Broker.MarketData;
using Skender.Stock.Indicators;
using Trader.Indicators;

namespace TradingBot.Strategies.Private.Indicators
{
    internal enum EMAandSMASignal
    {
        EMAMovingUpward, 
        EMAMovingDownward,
        SMAMovingUpward, 
        SMAMovingDownward,
        GoldenCross,
        DeathCross,
    }

    internal class EMAandSMAResult : ResultBase
    {
        public EMAandSMAResult(SmaResult smaResult, EmaResult emaResult)
        {
            SmaResult = smaResult;
            EmaResult = emaResult;
        }

        public SmaResult SmaResult { get; init; }
        public EmaResult EmaResult { get; init; }
    }

    // https://dotnet.stockindicators.dev/indicators/Ema/
    // https://dotnet.stockindicators.dev/indicators/Sma/
    internal class EMAandSMA : IndicatorBase<EMAandSMAResult, EMAandSMASignal>
    {
        int _nbPeriodsSMA;
        int _nbPeriodsEMA;

        public EMAandSMA(BarLength barLength, int nbPeriodsEMA=50, int nbPeriodsSMA=200) 
            : base(barLength, Math.Max(nbPeriodsSMA, nbPeriodsEMA), Math.Max(Math.Max(2* nbPeriodsEMA, nbPeriodsEMA + 100), nbPeriodsSMA))
        {
            _nbPeriodsEMA = nbPeriodsEMA;
            _nbPeriodsSMA = nbPeriodsSMA;
        }

        protected override IEnumerable<EMAandSMAResult> ComputeResults()
        {
            return _quotes.GetSma(_nbPeriodsSMA)
                .Zip(_quotes.GetEma(_nbPeriodsEMA))
                .Select(r => new EMAandSMAResult(r.First, r.Second));
        }

        protected override IEnumerable<EMAandSMASignal> ComputeSignals()
        {
            List<EMAandSMASignal> signals = new List<EMAandSMASignal>();
            if (IsReady && _results.Count() > 1 && _quotes.Count() > 1)
            {
                var latest = _results.Last();
                var previous = _results.SkipLast(1).Last();

                if(latest.EmaResult.Ema > previous.EmaResult.Ema)
                {
                    signals.Add(EMAandSMASignal.EMAMovingUpward);
                }
                else if (latest.EmaResult.Ema < previous.EmaResult.Ema)
                {
                    signals.Add(EMAandSMASignal.EMAMovingDownward);
                }

                if (latest.SmaResult.Sma > previous.SmaResult.Sma)
                {
                    signals.Add(EMAandSMASignal.SMAMovingUpward);
                }
                else if (latest.SmaResult.Sma < previous.SmaResult.Sma)
                {
                    signals.Add(EMAandSMASignal.SMAMovingDownward);
                }

                if (latest.EmaResult.Ema > latest.SmaResult.Sma && previous.EmaResult.Ema <= previous.SmaResult.Sma)
                {
                    signals.Add(EMAandSMASignal.GoldenCross);
                }
                else if (latest.EmaResult.Ema < latest.SmaResult.Sma && previous.EmaResult.Ema >= previous.SmaResult.Sma)
                {
                    signals.Add(EMAandSMASignal.DeathCross);
                }
            }
            return signals;
        }
    }
}
