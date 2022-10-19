using System;
using System.IO;

namespace HistoricalDataToDb
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
        }
    }

    class HistoricalDataToDb
    {
        public const string DbPath = "C:\\tradingbot\\db\\historicaldata.sqlite3";
        public const string JsonHistoricalDataRootDir = "D:\\historical";

        string _dbPath;
        string _jsonHistoricalDataRootDir;

        public HistoricalDataToDb(string dbPath, string jsonHistoricalDataRootDir)
        {
            if (!File.Exists(_dbPath)) throw new FileNotFoundException("Database file doesn't exists");
            if (!Directory.Exists(_jsonHistoricalDataRootDir)) throw new DirectoryNotFoundException("Root directory doesn't exists");
            
            _dbPath = dbPath;
            _jsonHistoricalDataRootDir = jsonHistoricalDataRootDir;


        }

        public void PopulateDb()
        {

            foreach (string dir in Directory.EnumerateDirectories(_jsonHistoricalDataRootDir))
            {
                PopulateDb(dir);
            }
        }

        public void PopulateDb(string symbol)
        {

        }
    }
}
