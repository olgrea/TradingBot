using Broker.MarketData;
using Skender.Stock.Indicators;
using Trader.Indicators;

namespace TradingBot.Strategies.Private.Indicators
{
    internal enum SqueezeSignal
    {
        WideSqueeze,
        NormalSqueeze,
        NarrowSqueeze,

        FiredWideSqueezeUp,
        FiredNormalSqueezeUp,
        FiredNarrowSqueezeUp,

        FiredWideSqueezeDown,
        FiredNormalSqueezeDown,
        FiredNarrowSqueezeDown,
    }

    internal class SqueezeResult : ResultBase
    {
        public SqueezeResult(KeltnerResult kc1x, KeltnerResult kc1_5x, KeltnerResult kc2x, BollingerBandsResult bb, SlopeResult sr)
        {
            BollingerBands = bb;
            KeltnerChannels = new KeltnerResult[3] {kc1x, kc1_5x, kc2x};
            SlopeResult = sr;
        }

        public BollingerBandsResult BollingerBands { get; init; }
        public KeltnerResult[] KeltnerChannels { get; init; }
        public SlopeResult SlopeResult{ get; init; }

    }

    internal class Squeeze : IndicatorBase<SqueezeResult, SqueezeSignal>
    {
        public Squeeze(BarLength barLength, int nbPeriods=20) 
            : base(barLength, nbPeriods, Math.Max(2 * nbPeriods, nbPeriods + 100))
        {

        }

        protected override IEnumerable<SqueezeResult> ComputeResults()
        {
            var kc1x = _quotes.GetKeltner(NbPeriods, 1.0, NbPeriods);
            var kc1_5x = _quotes.GetKeltner(NbPeriods, 1.5, NbPeriods);
            var kc2x = _quotes.GetKeltner(NbPeriods, 2.0, NbPeriods);
            var bb = _quotes.GetBollingerBands(NbPeriods, 2.0);
            var s = _quotes.GetSlope(NbPeriods);

            return kc1x.Zip(kc1_5x, kc2x)
                .Zip(bb, s)
                .Select(r => new SqueezeResult(r.First.First, r.First.Second, r.First.Third, r.Second, r.Third));
        }

        protected override IEnumerable<SqueezeSignal> ComputeSignals()
        {
            List<SqueezeSignal> signals = new();
            if (IsReady && _results.Count() > 1 && _quotes.Count() > 1)
            {
                var latest = _results.Last();
                double? latestLowBB = latest.BollingerBands.LowerBand;
                double? latestUpBB = latest.BollingerBands.UpperBand;

                var previous = _results.SkipLast(1).Last();
                double? previousLowBB = previous.BollingerBands.LowerBand;
                double? previousUpBB = previous.BollingerBands.UpperBand;

                var squeeze = new SqueezeSignal[3] { SqueezeSignal.NarrowSqueeze, SqueezeSignal.NormalSqueeze, SqueezeSignal.WideSqueeze};
                var firedSqueezeUp = new SqueezeSignal[3] { SqueezeSignal.FiredNarrowSqueezeUp, SqueezeSignal.FiredNormalSqueezeUp, SqueezeSignal.FiredWideSqueezeUp};
                var firedSqueezeDown = new SqueezeSignal[3] { SqueezeSignal.FiredNarrowSqueezeDown, SqueezeSignal.FiredNormalSqueezeDown, SqueezeSignal.FiredWideSqueezeDown};
                for (int i = 0; i < 2; i++)
                {
                    double? latestLowKC = latest.KeltnerChannels[i].LowerBand;
                    double? latestUpKC = latest.KeltnerChannels[i].UpperBand;
                    double? previousLowKC = previous.KeltnerChannels[i].LowerBand;
                    double? previousUpKC = previous.KeltnerChannels[i].UpperBand;

                    if (previousLowBB < previousLowKC && previousUpBB > previousUpKC
                        && latestLowBB >= latestLowKC && latestUpBB <= latestUpKC)
                    {
                        signals.Add(squeeze[i]);
                    }
                    else if (previousLowBB >= previousLowKC && previousUpBB <= previousUpKC
                        && latestLowBB < latestLowKC && latestUpBB > latestUpKC)
                    {
                        if(latest.SlopeResult.Slope > 0)
                            signals.Add(firedSqueezeUp[i]);
                        else
                            signals.Add(firedSqueezeDown[i]);
                    }
                }
            }

            return signals;
        }
    }
}
