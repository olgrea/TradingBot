using System;
using System.Threading.Tasks;
using CommandLine;
using TradingBot;
using TradingBot.Strategies;
using TradingBot.Utils;

namespace ConsoleApp
{
    internal class Program
    {
        public class Options
        {
            [Option('t', "ticker", Required = true, HelpText = "The symbol of the stock that will be traded.")]
            public string Ticker { get; set; } = "";

            [Option('s', "start", Required = true, HelpText = "The start time at which to start trading (format : HH:mm , HH is [0-23] and mm is [0-59]).")]
            public string StartTime { get; set; } = "";

            [Option('e', "end", Required = false, Default="15:55", HelpText = "The end time at which to stop trading. Default is 15h55. (format : HH:mm , HH is [0-23] and mm is [0-59]).")]
            public string EndTime { get; set; } = "";

            [Option('i', "clientId", Required = false, Default = 1337, HelpText = "The client id")]
            public int ClientId { get; set; }
        }

        static async Task Main(string[] args)
        {
            var parsedArgs = Parser.Default.ParseArguments<Options>(args);

            string ticker = parsedArgs.Value.Ticker;
            TimeSpan startTime = TimeSpan.Parse(parsedArgs.Value.StartTime);
            if (startTime < DateTimeUtils.MarketStartTime)
                throw new ArgumentException($"The start time must be at least {DateTimeUtils.MarketStartTime}");
            
            TimeSpan endTime = TimeSpan.Parse(parsedArgs.Value.EndTime);
            if (endTime > DateTimeUtils.MarketEndTime)
                throw new ArgumentException($"The end time must be at most {DateTimeUtils.MarketEndTime}");

            var start = new DateTime(DateTime.Today.Ticks + startTime.Ticks, DateTimeKind.Local);
            var end = new DateTime(DateTime.Today.Ticks + endTime.Ticks, DateTimeKind.Local);

            //var start = new DateTime(DateTime.Today.Ticks + DateTime.Now.AddSeconds(10).TimeOfDay.Ticks, DateTimeKind.Local);
            //var end = new DateTime(DateTime.Today.Ticks + DateTime.Now.AddSeconds(30).TimeOfDay.Ticks, DateTimeKind.Local);

            Trader trader = new Trader(ticker, start, end, parsedArgs.Value.ClientId);
            trader.AddStrategyForTicker<TestStrategy>();
            
            await trader.Start();
            trader.Stop();
        }
    }
}
