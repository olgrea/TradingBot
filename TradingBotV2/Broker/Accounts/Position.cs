using TradingBotV2.Broker.Contracts;

namespace TradingBotV2.Broker.Accounts
{
    public class Position
    {
        public Position() { }

        public Position(Position pos)
        {
            Contract = pos.Contract;
            PositionAmount = pos.PositionAmount;
            MarketPrice = pos.MarketPrice;
            MarketValue = pos.MarketValue;
            AverageCost = pos.AverageCost;
            UnrealizedPNL = pos.UnrealizedPNL;
            RealizedPNL = pos.RealizedPNL;
        }

        public Contract Contract { get; set; }
        public double PositionAmount { get; set; }
        public double MarketPrice { get; set; }
        public double MarketValue { get; set; }
        public double AverageCost { get; set; }
        public double UnrealizedPNL { get; set; }
        public double RealizedPNL { get; set; }

        public bool InAny() => PositionAmount > 0;
    }
}
