using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TradingBot.Broker.MarketData;

namespace TradingBot.Utils
{
    internal class BarsUtils
    {
        public static string MakeDailyBarsPath(string ticker, DateTime date) => Path.Combine(ticker, date.ToString("yyyy-MM-dd"), "full.json");
        public static string MakeHourlyBarsPath(string ticker, DateTime date) => Path.Combine(ticker, date.ToString("yyyy-MM-dd"), $"{date.ToString("yyyy-MM-dd HH-mm-ss")}.json");
        
        public static IEnumerable<Bar> DeserializeBars(string path)
        {
            var json = File.ReadAllText(path);
            return (IEnumerable<Bar>)JsonSerializer.Deserialize(json, typeof(IEnumerable<Bar>));
        }

        public static void SerializeBars(string path, IEnumerable<Bar> bars)
        {
            if(bars == null || !bars.Any())
                throw new ArgumentNullException(nameof(bars));

            var dirPath = Path.GetDirectoryName(path);
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            var json = JsonSerializer.Serialize(bars);
            File.WriteAllText(path, json);
        }

        public static Bar MakeBar(LinkedList<Bar> list, BarLength barLength)
        {
            int seconds = (int)barLength;

            Bar bar = new Bar() { High = double.MinValue, Low = double.MaxValue, BarLength = barLength };
            var e = list.GetEnumerator();
            e.MoveNext();

            // The 1st bar shouldn't be included.
            e.MoveNext();

            int nbBars = seconds / 5;
            for (int i = 0; i < nbBars; i++, e.MoveNext())
            {
                Bar current = e.Current;
                if (i == 0)
                {
                    bar.Close = current.Close;
                }

                bar.High = Math.Max(bar.High, current.High);
                bar.Low = Math.Min(bar.Low, current.Low);
                bar.Volume += current.Volume;
                bar.TradeAmount += current.TradeAmount;

                if (i == nbBars - 1)
                {
                    bar.Open = current.Open;
                    bar.Time = current.Time;
                }
            }

            return bar;
        }
    }
}
