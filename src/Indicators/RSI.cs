using System.Collections.Generic;
using System.Linq;
using Skender.Stock.Indicators;
using TradingBot.Broker.MarketData;

namespace TradingBot.Indicators
{
    internal class RSI : IndicatorBase
    {
        const double _oversoldThreshold = 30.0;
        const double _overboughtThreshold = 70.0;
        
        IEnumerable<RsiResult> _rsiResults;
        int _nbPeriods;

        public RSI(BarLength barLength, int nbPeriods) : base(barLength, 10*nbPeriods) 
        {
            _nbPeriods = nbPeriods;
        }

        public override int NbPeriods => _nbPeriods;

        public RsiResult RsiResult => _rsiResults?.LastOrDefault();

        public double Value => RsiResult?.Rsi ?? double.MinValue;

        public bool IsOverbought => IsReady && Value > _overboughtThreshold;
        public bool IsOversold => IsReady && Value < _oversoldThreshold;

        public override bool IsReady => base.IsReady && Value != double.MinValue;

        public override void Compute()
        {
            _rsiResults = Bars.GetRsi(NbPeriods);
        }
    }
}
