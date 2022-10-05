using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators.MovingAverages
{
    internal class ExponentialMovingAverage : MovingAverage
    {
        const int Smoothing = 2;
        SimpleMovingAverage _sma;

        public ExponentialMovingAverage(BarLength barLength, int nbPeriods = 14) : base(barLength, nbPeriods) 
        { 
            _sma = new SimpleMovingAverage(BarLength, nbPeriods);
            Value = double.MinValue;
        }

        public override bool IsReady => Value > double.MinValue && _sma.IsReady && base.IsReady;

        public override void Compute()
        {
            if (!_sma.IsReady)
            {
                // We need an SMA to start computing the EMA.
                _sma.Compute();
                return;
            }

            base.Compute(); 
        }

        public override double ComputeMA()
        {
            var latestBar = Bars.Last.Value;
            var current = (latestBar.Close + latestBar.High + latestBar.Low) / 3;
            var smoothing = Smoothing / (1 + NbPeriods);

            return current * smoothing + _last * (1 - smoothing);
        }
    }
}
