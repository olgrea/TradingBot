using System.Collections.Generic;
using InteractiveBrokers.Accounts;
using Skender.Stock.Indicators;
using TradingBot.Indicators;

namespace TradingBot.Strategies
{
    public interface IIndicatorStrategy
    {
        IEnumerable<IIndicator> Indicators { get; }
        void ComputeIndicators(IEnumerable<IQuote> quotes);
        TradeSignal GenerateTradeSignal(Position position);
    }
}
