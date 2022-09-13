using System.Collections.Generic;

namespace TradingBot.Broker.Client
{
    internal class DataSubscriptions
    {
        public bool AccountUpdates { get; set; }
        public bool Positions { get; set; }
        public Dictionary<Contract, int> BidAsk { get; set; } = new Dictionary<Contract, int>();
        public Dictionary<Contract, int> FiveSecBars{ get; set; } = new Dictionary<Contract, int>();
        public Dictionary<Contract, int> Pnl { get; set; } = new Dictionary<Contract, int>();
    }
}
