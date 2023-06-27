using Broker.MarketData;
using Skender.Stock.Indicators;
using Trader.Indicators;

namespace TradingBot.Strategies.Private.Indicators
{
    internal enum LinearRegressionSignal
    {
        TrendingUp,
        TrendingDown,
        TrendingSideway,
        SlopeRising,
        SlopeDescending,
        PriceAtTopChannel,
        PriceAtBottomChannel,
        CrossedUnderHighestFibLevel, 
        CrossedOverLowestFibLevel,
        OutOfTopChannel,
    }

    internal class LinearRegressionResult : ResultBase
    {
        public LinearRegressionResult(SlopeResult slopeResult, double?[] fibonacciLevels, double? topChannel, double? bottomChannel)
        {
            SlopeResult = slopeResult;
            FibonacciLevels = fibonacciLevels;
            TopChannel = topChannel;
            BottomChannel = bottomChannel;
        }

        public double?[] FibonacciLevels { get; init; }
        public double? HighestFibonacciLevel => FibonacciLevels.LastOrDefault();
        public double? LowestFibonacciLevel => FibonacciLevels.FirstOrDefault();

        public double? TopChannel { get; init; }
        public double? BottomChannel { get; init; }
        public SlopeResult SlopeResult { get; init; }
    }

    //https://dotnet.stockindicators.dev/indicators/Slope/
    internal class LinearRegression : IndicatorBase<LinearRegressionResult, LinearRegressionSignal>
    {
        readonly double[] FiboRatios = new double[4] { 0.236, 0.382, 0.618, 0.786 };

        public LinearRegression(BarLength barLength, int nbPeriods = 100) : base(barLength, nbPeriods, nbPeriods)
        {
        }

        protected override IEnumerable<LinearRegressionResult> ComputeResults()
        {
            return _quotes.GetSlope(NbPeriods).Select(r =>
            {
                double line = Convert.ToDouble(r.Line);
                double? dev = r.StdDev;
                int devlen = 2;

                // calculate channels
                double? top = line + dev * devlen;
                double? bottom = line - dev * devlen;

                // calculate fibonacci levels
                double?[] levels = new double?[4];
                for (int i = 0; i < 4; i++)
                {
                    levels[i] = line - dev * devlen + dev * devlen * 2 * FiboRatios[i];
                }

                return new LinearRegressionResult(r, levels, top, bottom);
            });
        }

        protected override IEnumerable<LinearRegressionSignal> ComputeSignals()
        {
            List<LinearRegressionSignal> signals = new();
            if (IsReady && _results.Count() > 1 && _quotes.Count() > 1)
            {
                var latestResult = _results.Last();
                var previousResult = _results.SkipLast(1).Last();

                if(latestResult.SlopeResult.Slope > previousResult.SlopeResult.Slope) 
                {
                    signals.Add(LinearRegressionSignal.SlopeRising);
                }
                else if (latestResult.SlopeResult.Slope < previousResult.SlopeResult.Slope)
                {
                    signals.Add(LinearRegressionSignal.SlopeDescending);
                }

                if(latestResult.SlopeResult.Slope.HasValue)
                {
                    double slopePrecision = 0.001;
                    if(Math.Abs(latestResult.SlopeResult.Slope.Value) < slopePrecision)
                    {
                        signals.Add(LinearRegressionSignal.TrendingSideway);
                    }
                    else if (latestResult.SlopeResult.Slope > 0)
                    {
                        signals.Add(LinearRegressionSignal.TrendingUp);
                    }
                    else
                    {
                        signals.Add(LinearRegressionSignal.TrendingDown);
                    }
                }

                double centPrecision = 0.01;
                var latestClose = Convert.ToDouble(_quotes.Last().Close);

                if(latestResult.TopChannel.HasValue && Math.Abs(latestClose - latestResult.TopChannel.Value) <= centPrecision)
                {
                    signals.Add(LinearRegressionSignal.PriceAtTopChannel);
                }
                else if (latestResult.BottomChannel.HasValue && Math.Abs(latestClose - latestResult.BottomChannel.Value) <= centPrecision)
                {
                    signals.Add(LinearRegressionSignal.PriceAtBottomChannel);
                }

                var previousClose = Convert.ToDouble(_quotes.SkipLast(1).Last().Close);
                if (previousResult.HighestFibonacciLevel.HasValue && previousClose >= previousResult.HighestFibonacciLevel.Value && latestClose < latestResult.HighestFibonacciLevel)
                {
                    signals.Add(LinearRegressionSignal.CrossedUnderHighestFibLevel);
                }
                else if (previousResult.LowestFibonacciLevel.HasValue && previousClose <= previousResult.LowestFibonacciLevel.Value && latestClose > latestResult.LowestFibonacciLevel)
                {
                    signals.Add(LinearRegressionSignal.CrossedOverLowestFibLevel);
                }
            }
            return signals;
        }
    }
}
