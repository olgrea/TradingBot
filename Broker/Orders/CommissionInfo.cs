namespace TradingBot.Broker.Orders
{
    public class CommissionInfo
    {
        public string ExecId { get; set; }
        public double Commission { get; set; }
        public string Currency { get; set; }
        public double RealizedPNL { get; set; }
        
        // TODO : useful?
        public double Yield { get; set; }
        public int YieldRedemptionDate { get; set; }
    }
}
