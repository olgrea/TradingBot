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
        public const string RootDir = @"D:\historical";

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

        public static Bar MakeBar(LinkedListNode<Bar> node, int nbBars)
        {
            BarLength barLength = (BarLength)Enum.ToObject(typeof(BarLength), (int)node.Value.BarLength * nbBars);
            
            Bar bar = new Bar() { High = double.MinValue, Low = double.MaxValue, BarLength = barLength };

            LinkedListNode<Bar> currNode = node;
            for (int i = 0; i < nbBars; i++)
            {
                Bar current = currNode.Value;
                if (i == 0)
                {
                    bar.Open = current.Open;
                    bar.Time = current.Time;
                }

                bar.High = Math.Max(bar.High, current.High);
                bar.Low = Math.Min(bar.Low, current.Low);
                bar.Volume += current.Volume;
                bar.TradeAmount += current.TradeAmount;

                if (i == nbBars - 1)
                {
                    bar.Close = current.Close;
                }

                currNode = currNode.Next;
            }

            return bar;
        }
    }
}
