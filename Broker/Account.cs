using System;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Broker
{
    public class Account
    {
        public string Code { get; set; }
        public DateTime Time { get; set; }
        public List<CashBalance> CashBalances { get; set; } = new List<CashBalance>();
        public List<Position> Positions { get; set; } = new List<Position>();
        public CashBalance RealizedPnL { get; set; }
        public CashBalance UnrealizedPnL { get; set; }
    }

    public class CashBalance
    {
        public CashBalance(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        public Decimal Amount { get; set; }
        public string Currency { get; set; }

        public override string ToString()
        {
            return $"{Amount:C} {Currency}";
        }
    }
}
