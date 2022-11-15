using System.Collections.Generic;
using System.Linq;
using InteractiveBrokers.Accounts;
using InteractiveBrokers.MarketData;
using Skender.Stock.Indicators;
using TradingBot.Indicators;

namespace TradingBot.Strategies
{
    public class BollingerBandsStrategy : IStrategy
    {
        IEnumerable<IQuote> _quotes;

        public BollingerBandsStrategy()
        {
            BollingerBands = new BollingerBands(BarLength._1Min);
            Indicators = new List<IIndicator>() { BollingerBands };
        }

        BollingerBands BollingerBands { get; set; }
        public IEnumerable<IIndicator> Indicators { get; private set; }
        IQuote LatestQuote => _quotes.Last();

        public void ComputeIndicators(IEnumerable<IQuote> quotes)
        {
            _quotes = quotes;
            BollingerBands.Compute(quotes);
        }

        public TradeSignal GenerateTradeSignal(Position position)
        {
            var signal = TradeSignal.Neutral;

            if ((double)LatestQuote.Close > BollingerBands.LatestResult.UpperBand)
            {
                return TradeSignal.StrongSell;
            }
            else if ((double)LatestQuote.Close < BollingerBands.LatestResult.LowerBand)
            {
                return TradeSignal.StrongBuy;
            }

            return signal;
        }
    }
}
