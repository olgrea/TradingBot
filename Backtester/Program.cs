using System;
using CommandLine;
[assembly: Fody.ConfigureAwait(false)]

namespace Backtester
{
    internal class Program
    {
        public class Options
        {
            [Option('t', "ticker", Required = true, HelpText = "The symbol of the stock for which to backtest a strategy")]
            public string Ticker { get; set; } = "";

            [Option('s', "start", Required = true, HelpText = "The start date at which to start the testing. Format : YYYY-MM-dd")]
            public string StartDate { get; set; } = "";

            [Option('e', "end", Required = false, HelpText = "When specified, testing will occur between the start and end date. Format : YYYY-MM-dd")]
            public string EndDate { get; set; } = "";
        }

        static void Main(string[] args)
        {
            var parsedArgs = Parser.Default.ParseArguments<Options>(args);

            string ticker = parsedArgs.Value.Ticker;
            DateTime startDate = DateTime.Parse(parsedArgs.Value.StartDate);
            DateTime endDate = startDate;
            if (!string.IsNullOrEmpty(parsedArgs.Value.EndDate))
                endDate = DateTime.Parse(parsedArgs.Value.EndDate);

            var bt = new Backtester(ticker, startDate, endDate);
            bt.Start();
        }
    }
}
