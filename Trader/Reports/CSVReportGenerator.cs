using System.Text;

namespace Trader.Reports
{
    public class CSVReportGenerator : IReportGenerator
    {
        public static void GenerateReport(string filepath, TradeResults results)
        {
            var sb = new StringBuilder();
            char separator = ';';
            string header = string.Join(separator, nameof(Trade.Action), nameof(Trade.Qty), nameof(Trade.Ticker), nameof(Trade.Price), nameof(Trade.Commission), nameof(Trade.Time));

            sb.AppendLine(header);
            foreach (var trade in results.Trades)
            {
                sb.AppendLine(string.Join(separator, trade.Action, trade.Qty, trade.Ticker, trade.Price, trade.Commission, trade.Time));
            }
            File.WriteAllText(filepath, sb.ToString());
        }
    }
}
