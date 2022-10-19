using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using TradingBot.Broker.MarketData;

namespace TradingBot.Utils
{
    public static class MarketDataUtils
    {
        public const string RootDir = @"C:\tradingbot\oldHistoricalData";
        public static TimeSpan MarketStartTime = new TimeSpan(9, 30, 0);
        public static TimeSpan MarketEndTime = new TimeSpan(16, 00, 0);

        public static bool IsWeekend(this DateTime dt) => dt.DayOfWeek == DayOfWeek.Sunday || dt.DayOfWeek == DayOfWeek.Saturday;

        // Doesn't take holidays into account
        public static bool IsMarketOpen()
        {
            var now = DateTime.Now;
            var timeOfday = now.TimeOfDay;
            return !now.IsWeekend() && timeOfday > MarketStartTime && timeOfday < MarketEndTime;
        }

        public static IEnumerable<(DateTime, DateTime)> GetMarketDays(DateTime start, DateTime end)
        {
            if (end <= start)
                yield break;

            DateTime marketStartTime = new DateTime(start.Year, start.Month, start.Day, MarketStartTime.Hours, MarketStartTime.Minutes, MarketStartTime.Seconds);
            DateTime marketEndTime = new DateTime(start.Year, start.Month, start.Day, MarketEndTime.Hours, MarketEndTime.Minutes, MarketEndTime.Seconds);

            if (start < marketStartTime)
                start = marketStartTime;

            int i = 0;
            DateTime current = start;
            while (current < end)
            {
                if (!IsWeekend(current))
                {
                    if (i == 0 && start < marketEndTime)
                        yield return (start, marketEndTime);
                    else if (i > 0)
                        yield return (marketStartTime, marketEndTime);
                }

                current = current.AddDays(1);
                marketStartTime = marketStartTime.AddDays(1);
                marketEndTime = marketEndTime.AddDays(1);
                i++;
            }

            if (!IsWeekend(end) && end > marketStartTime)
            {
                if (end > marketEndTime)
                    end = marketEndTime;

                yield return (marketStartTime, end);
            }
        }

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

        public static IEnumerable<TData> DeserializeData<TData>(string rootDir, string symbol, DateTime date) where TData : IMarketData, new()
        {
            return DeserializeData<TData>(Path.Combine(rootDir, MakeDailyDataPath<TData>(symbol, date)));
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
