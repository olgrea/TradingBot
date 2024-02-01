using Broker.Accounts;

namespace Broker.IBKR.Accounts
{
    public enum AccountValueKey
    {
        Time,
        AccountReady,
        CashBalance,
        UnrealizedPnL,
        RealizedPnL,
    }

    public record struct AccountValue(AccountValueKey Key, string Value, string? Currency)
    {
        public AccountValue(AccountValueKey Key, string Value) : this(Key, Value, null) { }

        public static explicit operator AccountValue(IBApi.AccountValue val)
        {
            if (!Enum.TryParse(val.Key, out AccountValueKey key))
                throw new NotImplementedException($"{val.Key}");

            return new AccountValue()
            {
                Key = key,
                Value = val.Value,
                Currency = val.Currency,
            };
        }
    }

    public class IBAccount : IAccount
    {
        public const double MinimumUSDCashBalance = 500.0d;

        public string Code { get; set; }
        public DateTime Time { get; set; }
        public Dictionary<string, double> CashBalances { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, Position> Positions { get; set; } = new Dictionary<string, Position>();
        public Dictionary<string, double> RealizedPnL { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, double> UnrealizedPnL { get; set; } = new Dictionary<string, double>();

        public IBAccount(string code)
        {
            Code = code;
        }

        public double USDCash
        {
            get => CashBalances["USD"];
            set => CashBalances["USD"] = value;
        }

        public bool IsReady { get; set; } = true;

        public double AvailableBuyingPower => Math.Max(USDCash - MinimumUSDCashBalance, 0);

        public override int GetHashCode()
        {
            return Time.GetHashCode();
        }

        public override bool Equals(object? obj)
        {
            var a = obj as IBAccount;
            return a != null && a.Time == Time;
        }
    }
}
