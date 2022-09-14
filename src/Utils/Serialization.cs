using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TradingBot.Broker.MarketData;

namespace TradingBot.Utils
{
    internal class Serialization
    {
        public static IEnumerable<Bar> DeserializeBars(string path)
        {
            var json = File.ReadAllText(path);
            return (IEnumerable<Bar>)JsonSerializer.Deserialize(json, typeof(IEnumerable<Bar>));
        }

        public static void SerializeBars(string path, IEnumerable<Bar> bars)
        {
            var json = JsonSerializer.Serialize(bars);
            File.WriteAllText(path, json);
        }
    }
}
