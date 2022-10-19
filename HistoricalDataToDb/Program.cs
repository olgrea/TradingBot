using System;
using System.Collections.Generic;
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
        
        public HistoricalDataToDb() : this(DbPath, JsonHistoricalDataRootDir) { }

        public HistoricalDataToDb(string dbPath, string jsonHistoricalDataRootDir)
        {
            if (!Directory.Exists(_jsonHistoricalDataRootDir)) throw new DirectoryNotFoundException("Root directory doesn't exists");
            if (!File.Exists(_dbPath)) throw new FileNotFoundException("Database file doesn't exists");

            using var connection = new SqliteConnection(dbPath);
            
            _dbPath = dbPath;
            _jsonHistoricalDataRootDir = jsonHistoricalDataRootDir;
        }
        
        public void PopulateDb()
        {
            foreach (string dir in Directory.EnumerateDirectories(_jsonHistoricalDataRootDir))
                PopulateDb(dir);
        }

        void PopulateDb(string symbol)
        {
            foreach (string dir in Directory.EnumerateDirectories(Path.Combine(_jsonHistoricalDataRootDir, symbol)))
            {
                switch (dir)
                {
                    case nameof(Bar): PopulateDb<Bar>(symbol, dir); break;
                    case nameof(BidAsk): PopulateDb<BidAsk>(symbol, dir); break;
                    default: break;
                }
            }
        }

        void PopulateDb<TMarketData>(string symbol, string marketDataDir) where TMarketData : IMarketData, new()
        {
            using var connection = new SqliteConnection(_dbPath);
            foreach (string dir in Directory.EnumerateDirectories(marketDataDir))
            {
                var datetime = DateTime.Parse(dir);
                var marketData = MarketDataUtils.DeserializeData<TMarketData>(_jsonHistoricalDataRootDir, symbol, datetime);
                InsertData(connection, symbol, marketData);
            }
        }

        string[] BarTableColumns = new string[] { "Ticker", "Date", "Time", "BarLength", "Open", "Close", "High", "Low" };
        string[] BidAskTableColumns = new string[] { "Ticker", "Date", "Time", "Bid", "BidSize", "Ask", "AskSize" };

        void InsertData<TMarketData>(SqliteConnection connection, string symbol, IEnumerable<TMarketData> data) where TMarketData : IMarketData, new()
        {
            using var transaction = connection.BeginTransaction();

            SqliteCommand command = CreateInsertCommand<TMarketData>(connection);
            
            command.Parameters["Ticker"].Value = symbol;
            foreach (TMarketData d in data)
            {
                PopulateCommandParams(command.Parameters, d);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        SqliteCommand CreateInsertCommand<TMarketData>(SqliteConnection connection) where TMarketData : IMarketData, new()
        {
            IEnumerable<string> columns = null;
            if (typeof(TMarketData) == typeof(Bar))
                columns = BarTableColumns;
            else if (typeof(TMarketData) == typeof(BidAsk))
                columns = BidAskTableColumns;
            else
                return null;
            
            string tableName = typeof(TMarketData).Name;
            IEnumerable<string> values = columns.Select(s => "$" + s);
            
            SqliteCommand command = connection.CreateCommand();
            command.CommandText = 
            $@"
                INSERT INTO {tableName} ({string.Join(',', columns)})
                VALUES ({string.Join(',', values)})
            ";

            foreach(string val in values)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = val;
                command.Parameters.Add(parameter);
            }

            return command;
        }

        void PopulateCommandParams<TMarketData>(SqliteParameterCollection parameters, TMarketData data) where TMarketData : IMarketData, new()
        {
            parameters["Date"].Value = data.Time.Date;
            parameters["Time"].Value = data.Time.TimeOfDay;

            if (data is Bar bar)
            {
                parameters["BarLength"].Value = (int)bar.BarLength;
                parameters["Open"].Value = bar.Open;
                parameters["Close"].Value = bar.Close;
                parameters["High"].Value = bar.High;
                parameters["Low"].Value = bar.Low;
            }
            else if (data is BidAsk ba)
            {
                parameters["Bid"].Value = ba.Bid;
                parameters["BidSize"].Value = ba.BidSize;
                parameters["Ask"].Value = ba.Ask;
                parameters["AskSize"].Value = ba.AskSize;
                
            }
        }
    }
}
