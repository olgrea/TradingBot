namespace TradingBotV2.Broker.Accounts
{
    public class Position
    {
        public string? Ticker { get; set; }
        public double PositionAmount { get; set; }
        public double MarketPrice { get; set; }
        public double MarketValue { get; set; }
        public double AverageCost { get; set; }
        public double UnrealizedPNL { get; set; }
        public double RealizedPNL { get; set; }

        public bool InAny() => PositionAmount > 0;

        public static explicit operator Position(IBApi.Position position)
        {
            return new Position()
            {
                Ticker = position.Contract.Symbol,
                PositionAmount = position.PositionAmount,
                MarketPrice = position.MarketPrice,
                MarketValue = position.MarketValue,
                AverageCost = position.AverageCost,
                UnrealizedPNL = position.UnrealizedPNL,
                RealizedPNL = position.RealizedPNL,
            };
        }
    }
}
