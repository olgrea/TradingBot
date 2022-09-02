using System;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Broker
{
    public class Position
    {
        public Contract Contract { get; set; }
        public double PositionAmount { get; set; }
        public double MarketPrice { get; set; }
        public double MarketValue { get; set; }
        public double AverageCost { get; set; }
        public double UnrealizedPNL { get; set; }
        public double RealizedPNL { get; set; }

        public override string ToString()
        {
            return $"{Contract} : {PositionAmount} {MarketPrice} {MarketValue} {AverageCost} {UnrealizedPNL} {RealizedPNL}";
        }
    }
}
