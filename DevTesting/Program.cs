using System;
using System.Threading.Tasks;
using CommandLine;
using InteractiveBrokers.MarketData;
using TradingBot;
using TradingBot.Strategies;

namespace ConsoleApp
{
    internal class Program
    {
        public class Options
        {
            [Option('t', "ticker", Required = true, HelpText = "The symbol of the stock that will be traded.")]
            public string Ticker { get; set; } = "";
        }

        static async Task Main(string[] args)
        {
            var parsedArgs = Parser.Default.ParseArguments<Options>(args);

            string ticker = parsedArgs.Value.Ticker;
            Trader trader = new Trader(ticker);
            trader.AddStrategy<TestStrategy>();
            
            await trader.Start();
            trader.Stop();
        }
    }
}
