using System.Collections.Generic;
using System.Linq;
using InteractiveBrokers.MarketData;
using Skender.Stock.Indicators;
using TradingBot.Indicators;

namespace TradingBot.Strategies
{
    public class RsiDivergenceStrategy : IStrategy
    {
        IEnumerable<IQuote> _quotes;

        public RsiDivergenceStrategy(Trader trader)
        {
            Trader = trader;
            BollingerBands = new BollingerBands(BarLength._1Min);
            RsiDivergence = new RsiDivergence(BarLength._1Min);
            Indicators = new List<IIndicator>() { BollingerBands, RsiDivergence };
            CurrentState = new NeutralState(this);
        }

        Trader Trader;
        BollingerBands BollingerBands { get; set; }
        RsiDivergence RsiDivergence { get; set; }
        public IEnumerable<IIndicator> Indicators { get; private set; }
        IQuote LatestQuote => _quotes.LastOrDefault();
        ITradeSignalState CurrentState { get; set; }

        public void ComputeIndicators(IEnumerable<IQuote> quotes)
        {
            _quotes = quotes;
            BollingerBands.Compute(quotes);
            RsiDivergence.Compute(quotes);
        }

        public TradeSignal GenerateTradeSignal()
        {
            return CurrentState.GenerateTradeSignal();
        }

        #region States

        class NeutralState : ITradeSignalState
        {
            RsiDivergenceStrategy _strategy;
            public NeutralState(RsiDivergenceStrategy strategy) => _strategy = strategy;

            public TradeSignal GenerateTradeSignal()
            {
                if (_strategy.RsiDivergence.FastRSI.IsOversold && _strategy.RsiDivergence.LatestResult.RSIDivergence < 0)
                {
                    _strategy.Trader.RequestLastTradedPricesUpdates(_strategy.RsiDivergence);
                    _strategy.CurrentState = new OverSoldState(_strategy);
                }
                else if (_strategy.RsiDivergence.FastRSI.IsOverbought && _strategy.RsiDivergence.LatestResult.RSIDivergence > 0)
                {
                    _strategy.Trader.RequestLastTradedPricesUpdates(_strategy.RsiDivergence);
                    _strategy.CurrentState = new OverBoughtState(_strategy);
                }

                return TradeSignal.Neutral;
            }
        }

        class OverSoldState : ITradeSignalState
        {
            RsiDivergenceStrategy _strategy;
            public OverSoldState(RsiDivergenceStrategy strategy) => _strategy = strategy;

            public TradeSignal GenerateTradeSignal()
            {
                if (_strategy.RsiDivergence.LatestTrendingResult.RSIDivergence > 0)
                {
                    _strategy.Trader.CancelLastTradedPricesUpdates(_strategy.RsiDivergence);
                    _strategy.CurrentState = new NeutralState(_strategy);
                    return TradeSignal.StrongBuy;
                }

                return TradeSignal.Neutral;
            }
        }

        class OverBoughtState : ITradeSignalState
        {
            RsiDivergenceStrategy _strategy;
            public OverBoughtState(RsiDivergenceStrategy strategy) => _strategy = strategy;

            public TradeSignal GenerateTradeSignal()
            {
                if (_strategy.RsiDivergence.LatestTrendingResult.RSIDivergence < 0)
                {
                    _strategy.Trader.CancelLastTradedPricesUpdates(_strategy.RsiDivergence);
                    _strategy.CurrentState = new NeutralState(_strategy);
                    return TradeSignal.StrongSell;
                }

                return TradeSignal.Neutral;
            }
        }

        #endregion States
    }
}
