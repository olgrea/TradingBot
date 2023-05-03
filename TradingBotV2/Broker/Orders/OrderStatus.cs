namespace TradingBotV2.Broker.Orders
{
    public class OrderStatus
    {
        internal RequestInfo Info { get; set; } = new RequestInfo();
        public Status Status { get; set; }
        public double Filled { get; set; }
        public double Remaining { get; set; }
        public double AvgFillPrice { get; set; }
        public double LastFillPrice { get; set; }
        public double MktCapPrice { get; set; }

        public override string ToString()
        {
            return $"[{Info.OrderId}] : {Status} filled={Filled:c} remaining={Remaining:c} avgFillprice={AvgFillPrice:c} lastFillPrice={LastFillPrice:c}";
        }

        public static explicit operator OrderStatus(IBApi.OrderStatus status)
        {
            return new OrderStatus()
            {
                Info = new RequestInfo()
                {
                    OrderId = status.OrderId,
                    ParentId = status.ParentId,
                    ClientId = status.ClientId,
                    PermId = status.PermId,
                },
                Status = !string.IsNullOrEmpty(status.Status) ? (Status)Enum.Parse(typeof(Status), status.Status) : Status.Unknown,
                Filled = status.Filled,
                Remaining = status.Remaining,
                AvgFillPrice = status.AvgFillPrice,
                LastFillPrice = status.LastFillPrice,
                MktCapPrice = status.MktCapPrice,
            };
        }
    }
}
