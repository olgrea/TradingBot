using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TradingBot.Broker.MarketData;

namespace TradingBot.Utils
{
    internal class Serialization
    {
        public static LinkedList<Bar> DeserializeBars(string path)
        {
            var json = File.ReadAllText(path);
            return (LinkedList<Bar>)JsonSerializer.Deserialize(json, typeof(LinkedList<Bar>));
        }

        public static void SerializeBars(string path, LinkedList<Bar> bars)
        {
            var json = JsonSerializer.Serialize(bars);
            File.WriteAllText(path, json);
        }
    }
}
