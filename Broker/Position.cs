using System;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Broker
{
    internal class Position
    {
        public Contract Contract { get; set; }
        public Decimal Price { get; set; }
        public Decimal MarketPrice { get; set; }
        public Decimal MarketValue { get; set; }
        public Decimal AverageCost { get; set; }
        public Decimal UnrealizedPNL { get; set; }
        public Decimal RealizedPNL { get; set; }
    }
}
