namespace TradingBotV2.Broker.Accounts
{
    public class Account
    {
        public const double MinimumUSDCashBalance = 500.0d;

        public string Code { get; set; }
        public TimeSpan Time { get; set; }
        public Dictionary<string, double> CashBalances { get; set; } = new Dictionary<string, double>();
        public List<Position> Positions { get; set; } = new List<Position>();
        public Dictionary<string, double> RealizedPnL { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, double> UnrealizedPnL { get; set; } = new Dictionary<string, double>();

        public double USDCash
        {
            get => CashBalances["USD"];
            set => CashBalances["USD"] = value;
        }

        public double AvailableBuyingPower => Math.Max(USDCash - MinimumUSDCashBalance, 0);

        public override int GetHashCode()
        {
            return Time.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            var a = obj as Account;
            return a != null && a.Time == Time;
        }
    }
}
