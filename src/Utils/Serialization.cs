using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TradingBot.Broker.MarketData;

namespace TradingBot.Utils
{
    internal class Serialization
    {
        public static TCollection DeserializeBars<TCollection>(string path) where TCollection : IEnumerable<Bar>
        {
            var json = File.ReadAllText(path);
            return (TCollection)JsonSerializer.Deserialize(json, typeof(TCollection));
        }

        public static void SerializeBars<TCollection>(string path, TCollection bars) where TCollection : IEnumerable<Bar>
        {
            var json = JsonSerializer.Serialize(bars);
            File.WriteAllText(path, json);
        }
    }
}
