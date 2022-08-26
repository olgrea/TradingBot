using System;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Broker
{
    internal class Position
    {
        public Contract Contract { get; set; }
        public Decimal PositionAmount { get; set; }
        public Decimal MarketPrice { get; set; }
        public Decimal MarketValue { get; set; }
        public Decimal AverageCost { get; set; }
        public Decimal UnrealizedPNL { get; set; }
        public Decimal RealizedPNL { get; set; }

        public override string ToString()
        {
            return $"{Contract} : {PositionAmount} {MarketPrice} {MarketValue} {AverageCost} {UnrealizedPNL} {RealizedPNL}";
        }
    }
}
