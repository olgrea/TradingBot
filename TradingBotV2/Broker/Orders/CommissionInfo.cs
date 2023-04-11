namespace TradingBotV2.Broker.Orders
{
    public class CommissionInfo
    {
        public string ExecId { get; set; }
        public double Commission { get; set; }
        public string Currency { get; set; }
        public double RealizedPNL { get; set; }

        public override string ToString()
        {
            return $"execId={ExecId} commission={Commission} {Currency} : realizedPnL={RealizedPNL}";
        }

        public static explicit operator CommissionInfo(IBApi.CommissionReport report)
        {
            return new CommissionInfo()
            {
                Commission = report.Commission,
                ExecId = report.ExecId,
                Currency = report.Currency,
                RealizedPNL = report.RealizedPNL,
            };
        }
    }
}
