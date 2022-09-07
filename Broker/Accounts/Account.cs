using System;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Broker.Accounts
{
    public class Account
    {
        public string Code { get; set; }
        public DateTime Time { get; set; }
        public Dictionary<string, double> CashBalances { get; set; } = new Dictionary<string, double>();
        public List<Position> Positions { get; set; } = new List<Position>();
        public Dictionary<string, double> RealizedPnL { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, double> UnrealizedPnL { get; set; } = new Dictionary<string, double>();
    }
}
