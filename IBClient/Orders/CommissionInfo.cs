namespace IBClient.Orders
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
    }
}
