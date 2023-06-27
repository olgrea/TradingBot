namespace Broker.Orders
{
    public class CommissionInfo
    {
        public CommissionInfo(string execId)
        {
            ExecId = execId;
        }

        public string ExecId { get; set; }
        public double Commission { get; set; }
        public string? Currency { get; set; }
        public double? RealizedPNL { get; set; }

        public override string ToString()
        {
            string realizedPNL = RealizedPNL == null ? string.Empty : $"realizedPnL={RealizedPNL:c}";
            return $"commission={Commission:C} {Currency} {realizedPNL} (execId={ExecId})";
        }

        public static explicit operator CommissionInfo(IBApi.CommissionReport report)
        {
            return new CommissionInfo(report.ExecId)
            {
                Commission = report.Commission,
                Currency = report.Currency,
                RealizedPNL = report.RealizedPNL == double.MaxValue ? null : report.RealizedPNL,
            };
        }
    }
}
