namespace Broker.Accounts
{
    public interface IAccount
    {
        public string Code { get; set; }
        public double AvailableBuyingPower { get; }
        public Dictionary<string, double> CashBalances { get; set; }
        public Dictionary<string, Position> Positions { get; set; }
        public Dictionary<string, double> RealizedPnL { get; set; }
        public Dictionary<string, double> UnrealizedPnL { get; set; }
    }
}
