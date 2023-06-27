namespace Trader.Reports
{
    internal interface IReportGenerator
    {
        public static abstract void GenerateReport(string filepath, TradeResults results);
    }
}
