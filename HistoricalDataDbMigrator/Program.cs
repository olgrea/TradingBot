using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using TradingBot.Broker.MarketData;
using TradingBot.Utils;

namespace HistoricalDataToDb
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var hToDb = new HistoricalDataToDb();
            hToDb.PopulateDb();
        }
    }

    class HistoricalDataToDb
    {
        public const string DbPath = DbUtils.DbPath;
        public const string JsonHistoricalDataRootDir = MarketDataUtils.RootDir;

        string _dbPath;
        string _jsonHistoricalDataRootDir;
        string ConnectionStr => $"Data Source={_dbPath}";

        public HistoricalDataToDb() : this(DbPath, JsonHistoricalDataRootDir) { }

        public HistoricalDataToDb(string dbPath, string jsonHistoricalDataRootDir)
        {
            if (!Directory.Exists(jsonHistoricalDataRootDir)) throw new DirectoryNotFoundException("Root directory doesn't exists");
            if (!File.Exists(dbPath)) throw new FileNotFoundException("Database file doesn't exists");

            _dbPath = dbPath;
            _jsonHistoricalDataRootDir = jsonHistoricalDataRootDir;
        }
        
        public void PopulateDb()
        {
            Console.WriteLine($"Beginning migration of historical data to SQLite db.");
            foreach (string dir in Directory.EnumerateDirectories(_jsonHistoricalDataRootDir))
                PopulateDb(dir);
            Console.WriteLine($"\nComplete!");
        }

        void PopulateDb(string symbolDirPath)
        {
            string symbol = Path.GetFileName(symbolDirPath);
            Console.WriteLine($"Starting insertion of {symbol} historical data into db {_dbPath}");
            foreach (string dir in Directory.EnumerateDirectories(symbolDirPath))
            {
                switch (Path.GetFileName(dir))
                {
                    case nameof(Bar): PopulateDb<Bar>(symbol, dir); break;
                    case nameof(BidAsk): PopulateDb<BidAsk>(symbol, dir); break;
                    default: break;
                }
            }
            Console.WriteLine($"Done!");
        }

        void PopulateDb<TMarketData>(string symbol, string marketDataDir) where TMarketData : IMarketData, new()
        {
            using var connection = new SqliteConnection(ConnectionStr);
            connection.Open();

            foreach (string dir in Directory.EnumerateDirectories(marketDataDir))
            {
                var datetime = DateTime.Parse(Path.GetFileName(dir));

                IEnumerable<TMarketData> marketData = null;
                try
                {
                    marketData = MarketDataUtils.DeserializeData<TMarketData>(_jsonHistoricalDataRootDir, symbol, datetime);
                }
                catch (FileNotFoundException)                
                {
                    Console.WriteLine($"{symbol} {typeof(TMarketData).Name} {datetime.ToShortDateString()} : Incomplete data. 'full.json' file missing. Skipping.");
                    continue;
                }

                if(DbUtils.DataExistsInDb<TMarketData>(symbol, datetime, connection))
                {
                    Console.WriteLine($"{symbol} {typeof(TMarketData).Name} {datetime.ToShortDateString()} : historical data already inserted. Skipping.");
                    continue;
                }

                Console.Write($"{symbol} {typeof(TMarketData).Name} {datetime.ToShortDateString()} : Inserting historical data...");
                DbUtils.InsertData(symbol, marketData, connection);
                Console.Write($" inserted!\n");
            }
        }
    }
}
