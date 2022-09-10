namespace TradingBot.Broker.Accounts
{
    internal class PnL
    {
        public Contract Contract { get; set; }
        public double PositionAmount { get; set; }
        public double MarketValue { get; set; }
        public double DailyPnL { get; set; }
        public double UnrealizedPnL { get; set; }
        public double RealizedPnL { get; set; }

        public override string ToString()
        {
            return $"{Contract} : {PositionAmount} {MarketValue} {DailyPnL} {UnrealizedPnL} {RealizedPnL}";
        }
    }
}
