namespace TradingBot.Broker.Accounts
{
    public record struct PnL(string Ticker, decimal Pos, double DailyPnL, double UnrealizedPnL, double RealizedPnL, double MarketValue)
    {
        public static explicit operator PnL(IBApi.PnL pnl)
        {
            return new PnL()
            {
                Ticker = pnl.Ticker,
                Pos = pnl.Pos,
                DailyPnL= pnl.DailyPnL,
                UnrealizedPnL= pnl.UnrealizedPnL,
                RealizedPnL= pnl.RealizedPnL,
                MarketValue = pnl.MarketValue,
            };
        }

        public override string? ToString()
        {
            var unrealized = UnrealizedPnL == double.MaxValue ? 0.0 : UnrealizedPnL;
            var realized = RealizedPnL == double.MaxValue ? 0.0 : RealizedPnL;
            var daily = DailyPnL == double.MaxValue ? 0.0 : DailyPnL;
            return $"{Pos} {Ticker} : unrealized {unrealized:c} realized : {realized:c} daily : {daily:c}";
        }
    }

    public class Position
    {
        public Position(string ticker)
        {
            Ticker = ticker;
        }

        public string Ticker { get; set; }
        public double PositionAmount { get; set; }
        public double Price { get; set; }
        public double TotalMarketValue { get; set; }
        public double AverageCost { get; set; }
        public double UnrealizedPNL { get; set; }
        public double RealizedPNL { get; set; }

        public static explicit operator Position(IBApi.Position position)
        {
            return new Position(position.Contract.Symbol)
            {
                PositionAmount = Convert.ToDouble(position.PositionAmount),
                Price = position.MarketPrice,
                TotalMarketValue = position.MarketValue,
                AverageCost = position.AverageCost,
                UnrealizedPNL = position.UnrealizedPNL,
                RealizedPNL = position.RealizedPNL,
            };
        }

        public PnL ToPnL()
        {
            return new PnL(Ticker, (int)PositionAmount, RealizedPNL + UnrealizedPNL, UnrealizedPNL, RealizedPNL, TotalMarketValue);
        }

        public override string ToString()
        {
            return $"{PositionAmount} {Ticker} at {Price:c} (avgCost={AverageCost:c}, total={TotalMarketValue:c})";
        }
    }
}
