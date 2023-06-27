using Broker.MarketData;
using Skender.Stock.Indicators;
using Trader.Indicators;

namespace TradingBot.Private.Indicators
{
    //internal enum PivotType
    //{
    //    High, Low,
    //}

    //internal class Pivot
    //{
    //    public Pivot(IQuote quote, PivotType type, int order)
    //    {
    //        Quote = quote;
    //        Type = type;
    //        Order = order;
    //    }

    //    public IQuote Quote { get; }
    //    public PivotType Type { get; }
    //    public int Order { get; }

    //    public static (IEnumerable<Pivot>, IEnumerable<Pivot>)? Find(IEnumerable<IQuote> quotes, int order = 3)
    //    {
    //        if (order < 1)
    //            return null;

    //        List<Pivot> low = quotes.Select(q => new Pivot(q, PivotType.Low, 0)).ToList();
    //        List<Pivot> high = quotes.Select(q => new Pivot(q, PivotType.High, 0)).ToList();
    //        for (int i = 1; i <= order; ++i)
    //        {
    //            low = FindNextOrderPivot(low);
    //            high = FindNextOrderPivot(high);
    //        }

    //        return (low, high);
    //    }

    //    internal static List<Pivot> FindNextOrderPivot(List<Pivot> pivots)
    //    {
    //        List<Pivot> nextOrderPivots = new();
    //        if(pivots.Count < 2) 
    //            return nextOrderPivots;

    //        for (int i = 2; i < pivots.Count; ++i)
    //        {
    //            if (pivots[i].Type == PivotType.High 
    //                && pivots[i-2].Quote.High < pivots[i - 1].Quote.High && pivots[i - 1].Quote.High > pivots[i].Quote.High)
    //            {
    //                nextOrderPivots.Add(new Pivot(pivots[i - 1].Quote, PivotType.High, pivots[i - 1].Order + 1));
    //            }
    //            else if (pivots[i].Type == PivotType.Low
    //                && pivots[i - 2].Quote.Low > pivots[i - 1].Quote.Low && pivots[i - 1].Quote.Low < pivots[i].Quote.Low)
    //            {
    //                nextOrderPivots.Add(new Pivot(pivots[i - 1].Quote, PivotType.High, pivots[i - 1].Order + 1));
    //            }
    //        }

    //        return nextOrderPivots;
    //    }
    //}

    internal class Pivot : IndicatorBase<PivotsResult, int>
    {
        int _leftSpan;
        int _rightSpan;
        int _maxTrendPeriod;

        public Pivot(BarLength barLength, int leftSpan=2, int rightSpan=2, int maxTrendPeriod=20) 
            : base(barLength, leftSpan+rightSpan+1, leftSpan + rightSpan + 1)
        {
            _leftSpan = leftSpan;
            _rightSpan = rightSpan;
            _maxTrendPeriod = maxTrendPeriod;
        }

        protected override IEnumerable<PivotsResult> ComputeResults()
        {
            return _quotes.GetPivots(_leftSpan, _rightSpan, _maxTrendPeriod, EndType.Close);
        }

        protected override IEnumerable<int> ComputeSignals()
        {
            return Enumerable.Empty<int>();
        }
    }
}
