using System;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Broker
{
    internal class Account
    {
        public string Code { get; set; }
        public DateTime Time { get; set; }
        public string Currency { get; set; }
        public Decimal Cash { get; set; }
        public List<Position> Positions { get; set; } = new List<Position>();
        public Decimal RealizedPnL { get; set; }
        public Decimal UnrealizedPnL { get; set; }
    }
}
