using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
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
        public const string DbPath = "C:\\tradingbot\\db\\historicaldata.sqlite3";
        public const string JsonHistoricalDataRootDir = "D:\\historical";

        string _dbPath;
        string _jsonHistoricalDataRootDir;
        SqliteConnection _connection;
        string ConnectionStr => $"Data Source={_dbPath}";

        public HistoricalDataToDb() : this(DbPath, JsonHistoricalDataRootDir) { }

        public HistoricalDataToDb(string dbPath, string jsonHistoricalDataRootDir)
        {
            if (!Directory.Exists(jsonHistoricalDataRootDir)) throw new DirectoryNotFoundException("Root directory doesn't exists");
            if (!File.Exists(dbPath)) throw new FileNotFoundException("Database file doesn't exists");

            _dbPath = dbPath;
            _jsonHistoricalDataRootDir = jsonHistoricalDataRootDir;
            _connection = new SqliteConnection(ConnectionStr);
        }
        
        public void PopulateDb()
        {
            try
            {
                Console.WriteLine($"Beginning migration of historical data to SQLite db.");
                _connection.Open();
                foreach (string dir in Directory.EnumerateDirectories(_jsonHistoricalDataRootDir))
                    PopulateDb(dir);
                Console.WriteLine($"\nComplete!");
            }
            finally
            {
                _connection?.Dispose();
            }
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

                if(Exists<TMarketData>(symbol, datetime))
                {
                    Console.WriteLine($"{symbol} {typeof(TMarketData).Name} {datetime.ToShortDateString()} : historical data already inserted. Skipping.");
                    continue;
                }

                Console.Write($"{symbol} {typeof(TMarketData).Name} {datetime.ToShortDateString()} : Inserting historical data...");
                InsertData(symbol, marketData);
                Console.Write($" inserted!\n");
            }
        }

        bool Exists<TMarketData>(string symbol, DateTime datetime) where TMarketData : IMarketData, new ()
        {
            string tableName = typeof(TMarketData).Name;
            SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
            $@"
                SELECT EXISTS (
                    SELECT 1 FROM {tableName}
                        WHERE Ticker = '{symbol}'
                        AND Date = '{datetime.Date.ToShortDateString()}'
            );       
            ";

            using SqliteDataReader reader = command.ExecuteReader();

            // For some reason this query returns a long...
            var v = reader.Cast<IDataRecord>().First().GetInt64(0);
            return Convert.ToBoolean(v);
        }

        void InsertData<TMarketData>(string symbol, IEnumerable<TMarketData> data) where TMarketData : IMarketData, new()
        {
            using var transaction = _connection.BeginTransaction();

            SqliteCommand command = CreateInsertCommand<TMarketData>();
            
            command.Parameters["$Ticker"].Value = symbol;
            foreach (TMarketData d in data)
            {
                PopulateCommandParams(command.Parameters, d);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        IEnumerable<string> GetColumns<TMarketData>()
        {
            SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
            $@"
                PRAGMA table_info({typeof(TMarketData).Name});
            ";

            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Cast<IDataRecord>().Select(dr => dr.GetString(1)).ToList();
        }

        SqliteCommand CreateInsertCommand<TMarketData>() where TMarketData : IMarketData, new()
        {
            string tableName = typeof(TMarketData).Name;
            IEnumerable<string> columns = GetColumns<TMarketData>();
            IEnumerable<string> values = columns.Select(s => "$" + s);

            SqliteCommand readerCommand = _connection.CreateCommand();

            readerCommand.CommandText =
            $@"
                INSERT INTO {tableName} ({string.Join(',', columns)})
                VALUES ({string.Join(',', values)})
            ";

            foreach (string val in values)
            {
                var parameter = readerCommand.CreateParameter();
                parameter.ParameterName = val;
                readerCommand.Parameters.Add(parameter);
            }

            return readerCommand;
        }

        void PopulateCommandParams<TMarketData>(SqliteParameterCollection parameters, TMarketData data) where TMarketData : IMarketData, new()
        {
            parameters["$Date"].Value = data.Time.Date.ToShortDateString();
            parameters["$Time"].Value = data.Time.TimeOfDay;

            if (data is Bar bar)
            {
                parameters["$BarLength"].Value = (int)bar.BarLength;
                parameters["$Open"].Value = bar.Open;
                parameters["$Close"].Value = bar.Close;
                parameters["$High"].Value = bar.High;
                parameters["$Low"].Value = bar.Low;
            }
            else if (data is BidAsk ba)
            {
                parameters["$Bid"].Value = ba.Bid;
                parameters["$BidSize"].Value = ba.BidSize;
                parameters["$Ask"].Value = ba.Ask;
                parameters["$AskSize"].Value = ba.AskSize;
            }
        }
    }
}
