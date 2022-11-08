using System;
using IBApi;

namespace InteractiveBrokers.Orders
{
    public class OrderStatus
    {
        public RequestInfo Info { get; set; }
        public Status Status { get; set; }
        public double Filled { get; set; }
        public double Remaining { get; set; }
        public double AvgFillPrice { get; set; }
        public double LastFillPrice { get; set; }
        public double MktCapPrice { get; set; }

        public override string ToString()
        {
            return $"{Info.OrderId} {Status} : filled={Filled:c} remaining={Remaining:c} avgFillprice={AvgFillPrice:c} lastFillPrice={LastFillPrice:c}";
        }
    }
}
