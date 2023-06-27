using Broker.MarketData;
using Skender.Stock.Indicators;
using Trader.Indicators;

namespace TradingBot.Strategies.Private.Indicators
{
    internal enum MacdSignal
    {
        MACDCrossAboveSignalLine,
        MACDCrossBelowSignalLine,
        MACDAbove0Rising,
        MACDAbove0Falling,
        MACDBelow0Falling,
        MACDBelow0Rising,
        MACDNewHigh,
        MACDNewLow,
        DivergenceNewHigh,
        DivergenceNewLow,
    }

    // https://dotnet.stockindicators.dev/indicators/Macd/
    internal class MACD : IndicatorBase<MacdResult, MacdSignal>
    {
        int _fastPeriods;
        int _slowPeriods;
        int _signalPeriods;

        public MACD(BarLength barLength, int fastPeriods = 12, int slowPeriods = 26, int signalPeriods = 9)
            : base(barLength, slowPeriods, Math.Max(2 * (fastPeriods + slowPeriods), fastPeriods + slowPeriods + 100))
        {
            _fastPeriods = fastPeriods;
            _slowPeriods = slowPeriods;
            _signalPeriods = signalPeriods;
        }

        //  Convergence warning: The first S+P+250 periods will have decreasing magnitude, convergence-related
        //  precision errors that can be as high as ~5% deviation in indicator values for earlier periods.
        protected override IEnumerable<MacdResult> ComputeResults()
        {
            return _quotes.GetMacd(_fastPeriods, _slowPeriods, _signalPeriods);
        }

        protected override IEnumerable<MacdSignal> ComputeSignals()
        {
            List<MacdSignal> signals = new List<MacdSignal>();
            if (IsReady && _results.Count() > 1 && _quotes.Count() > 1)
            {
                var latest = _results.Last();
                var previous = _results.SkipLast(1).Last();

                if (latest.Macd > latest.Signal && previous.Macd <= previous.Signal)
                {
                    signals.Add(MacdSignal.MACDCrossAboveSignalLine);
                }
                else if (latest.Macd < latest.Signal && previous.Macd >= previous.Signal)
                {
                    signals.Add(MacdSignal.MACDCrossBelowSignalLine);
                }

                if (latest.Macd - latest.Signal >= 0)
                {
                    if (latest.Macd > previous.Macd)
                        signals.Add(MacdSignal.MACDAbove0Rising);
                    else
                        signals.Add(MacdSignal.MACDAbove0Falling);
                }
                else
                {
                    if (latest.Macd > previous.Macd)
                        signals.Add(MacdSignal.MACDBelow0Rising);
                    else
                        signals.Add(MacdSignal.MACDBelow0Falling);
                }
            }
            return signals;
        }
    }
}
