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
    }
}
