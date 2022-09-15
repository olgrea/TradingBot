using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TradingBot.Broker.MarketData;

namespace TradingBot.Utils
{
    internal class MarketDataUtils
    {
        public static string MakeDailyDataPath<TData>(string ticker, DateTime date) where TData : IMarketData, new()
        {
            return Path.Combine(ticker, typeof(TData).Name, date.ToString("yyyy-MM-dd"), "full.json");
        }

        public static string MakeDataPath<TData>(string ticker, DateTime date) where TData : IMarketData, new()
        {
            return Path.Combine(ticker, typeof(TData).Name, date.ToString("yyyy-MM-dd"), $"{date.ToString("yyyy-MM-dd HH-mm-ss")}.json");
        }

        public static IEnumerable<TData> DeserializeData<TData>(string path) where TData : IMarketData, new()
        {
            var json = File.ReadAllText(path);
            return (IEnumerable<TData>)JsonSerializer.Deserialize(json, typeof(IEnumerable<TData>));
        }

        public static void SerializeData<TData>(string path, IEnumerable<TData> data) where TData : IMarketData, new()
        {
            if(data == null)
                throw new ArgumentNullException(nameof(data));

            var dirPath = Path.GetDirectoryName(path);
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            var json = JsonSerializer.Serialize(data);
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
