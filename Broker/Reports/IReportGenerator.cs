namespace Broker.Reports
{
    internal interface IReportGenerator
    {
        public static abstract void GenerateReport(string filepath, TradeResults results);
    }
}
