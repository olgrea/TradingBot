using System.Text;

namespace TradingBot.Reports
{
    /// <summary>
    /// Results from trading are coded into an indicator that can be displayed in TradingView
    /// </summary>
    public class TradingViewIndicatorGenerator : IReportGenerator
    {
        const string header = "//@version=5\r\nindicator(title=\"trades\", shorttitle=\"trades\", overlay=true, max_lines_count=500, max_labels_count=500)";

        const string labelFormat = "label.new(timestamp(\"{0}-04:00\"), na, text=\"{1}\", xloc=xloc.bar_time, yloc={2}, color = color.white, textcolor=color.gray, size=size.large, style={3})";

        const string dateFormat = "yyyy-MM-ddTHH:mm:ss";

        const string buyYLocBar = "yloc.belowbar";
        const string sellYLocBar = "yloc.abovebar";
        const string buyArrowStyle = "label.style_arrowup";
        const string sellArrowStyle = "label.style_arrowdown";

        public static void GenerateReport(string filepath, TradeResults results)
        {
            var sb = new StringBuilder();
            sb.AppendLine(header);
            foreach (var trade in results.Trades)
            {
                if (trade.Action == Broker.Orders.OrderAction.BUY)
                    sb.AppendLine(string.Format(labelFormat, trade.Time.ToString(dateFormat), $"BUY {Convert.ToInt32(trade.Qty)} at {trade.Price:c}", buyYLocBar, buyArrowStyle));
                else
                    sb.AppendLine(string.Format(labelFormat, trade.Time.ToString(dateFormat), $"SELL {Convert.ToInt32(trade.Qty)} at {trade.Price:c}", sellYLocBar, sellArrowStyle));
            }
            File.WriteAllText(filepath, sb.ToString());
        }
    }

    //var results = new TradeResults()
    //{
    //    Trades =
    //    {
    //        new Trade(TradingBot.Broker.Orders.OrderAction.BUY, 1074, "GME", 22.78, 5.37, DateTime.Parse("2023-05-18 09:50:57")),
    //        new Trade(TradingBot.Broker.Orders.OrderAction.SELL, 1074, "GME", 23.18, 5.37, DateTime.Parse("2023-05-18 10:48:02")),
    //        new Trade(TradingBot.Broker.Orders.OrderAction.BUY, 1074, "GME", 23.19, 5.37, DateTime.Parse("2023-05-18 11:09:54")),
    //        new Trade(TradingBot.Broker.Orders.OrderAction.SELL, 1074, "GME", 23.21, 5.37, DateTime.Parse("2023-05-18 11:44:57")),
    //        new Trade(TradingBot.Broker.Orders.OrderAction.BUY, 1081, "GME", 23.04, 5.405, DateTime.Parse("2023-05-18 11:58:57")),
    //        new Trade(TradingBot.Broker.Orders.OrderAction.SELL, 1081, "GME", 23.03, 5.405, DateTime.Parse("2023-05-18 13:06:08")),
    //        new Trade(TradingBot.Broker.Orders.OrderAction.BUY, 1083, "GME", 22.98, 5.415, DateTime.Parse("2023-05-18 14:00:12")),
    //        new Trade(TradingBot.Broker.Orders.OrderAction.SELL, 1083, "GME", 23.16, 5.415, DateTime.Parse("2023-05-18 15:19:10")),
    //        new Trade(TradingBot.Broker.Orders.OrderAction.BUY, 1086, "GME", 23.08, 5.43, DateTime.Parse("2023-05-18 15:37:56")),
    //        new Trade(TradingBot.Broker.Orders.OrderAction.SELL, 1086, "GME", 23.03, 5.43, DateTime.Parse("2023-05-18 15:59:59")),
    //    }
    //};
}
