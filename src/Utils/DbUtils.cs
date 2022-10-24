// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using TradingBot.Broker.MarketData;

namespace TradingBot.Utils
{
    public static  class DbUtils
    {
        public const string DbPath = @"C:\tradingbot\db\historicaldata.sqlite3";

        public static bool DataExistsInDb<TMarketData>(string symbol, DateTime date) where TMarketData : IMarketData, new()
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();
            return DataExistsInDb<TMarketData>(symbol, date, connection);
        }

        public static bool DataExistsInDb<TMarketData>(string symbol, DateTime date, SqliteConnection connection) where TMarketData : IMarketData, new()
        {
            return DataExistsInDb<TMarketData>(symbol, date, default((TimeSpan, TimeSpan)), connection);
        }

        public static bool DataExistsInDb<TMarketData>(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange) where TMarketData : IMarketData, new()
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();
            return DataExistsInDb<TMarketData>(symbol, date, timeRange, connection);
        }

        public static bool DataExistsInDb<TMarketData>(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange, SqliteConnection connection) where TMarketData : IMarketData, new()
        {
            string tableName = typeof(TMarketData).Name;
            SqliteCommand command = connection.CreateCommand();

            string AND_Time = "";
            if(timeRange != default)
            {
                AND_Time = $"AND Time >= '{timeRange.Item1}' AND Time < '{timeRange.Item2}'";
            }

            command.CommandText =
            $@"
                SELECT EXISTS (
                    SELECT 1 FROM Historical{tableName}View
                        WHERE Ticker = '{symbol}'
                        AND Date = '{date.Date.ToShortDateString()}'
                        {AND_Time}
                );
            ";

            using SqliteDataReader reader = command.ExecuteReader();

            // For some reason this query returns a long...
            var v = reader.Cast<IDataRecord>().First().GetInt64(0);
            return Convert.ToBoolean(v);
        }

        public static void InsertData<TMarketData>(string symbol, IEnumerable<TMarketData> data) where TMarketData : IMarketData, new()
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();
            InsertData(symbol, data, connection);
        }

        public static void InsertData<TMarketData>(string symbol, IEnumerable<TMarketData> dataCollection, SqliteConnection connection) where TMarketData : IMarketData, new()
        {
            using var transaction = connection.BeginTransaction();

            SqliteCommand tickerCmd = connection.CreateCommand();
            SqliteCommand dateCmd = connection.CreateCommand();
            SqliteCommand timeCmd = connection.CreateCommand();
            SqliteCommand marketDataCmd = connection.CreateCommand();
            SqliteCommand finalCmd = connection.CreateCommand();

            InsertFromValue(tickerCmd, "Stock", "Symbol", symbol);

            foreach (TMarketData data in dataCollection)
            {
                InsertFromValue(dateCmd, "Date", "Date", data.Time.Date.ToShortDateString());
                InsertFromValue(timeCmd, "Time", "Time", data.Time.TimeOfDay.ToString());

                object[] values = null;
                if (data is Bar bar)
                    values = new object[] { bar.Open, bar.Close, bar.High, bar.Low, bar.Volume };
                else if (data is BidAsk ba)
                    values = new object[] { ba.Bid, ba.BidSize, ba.Ask, ba.AskSize};

                string tableName = data.GetType().Name;
                InsertFromValues(marketDataCmd, tableName, GetColumns(connection, tableName).ToArray(), values);

                tableName = $"Historical{data.GetType().Name}";
                InsertFromSelect(finalCmd, tableName, GetColumns(connection, tableName).ToArray(), MakeSelectForInsert(symbol, data));
            }

            transaction.Commit();
        }

        static void InsertFromValue(SqliteCommand command, string tableName, string column, object value)
        {
            InsertFromValues(command, tableName, new[] { column }, new[] { value });
        }

        static void InsertFromValues(SqliteCommand command, string tableName, string[] columns, object[] values)
        {
            command.CommandText =
            $@"
                INSERT OR IGNORE INTO {tableName} ({string.Join(',', columns)})
                VALUES ({string.Join(',', values.Select(v =>
                {
                    if (v is string)
                        return $"'{v}'";
                    else if (v is double d)
                        return d.ToString(CultureInfo.InvariantCulture);
                    else
                        return v.ToString();
                }))})
            ";

            command.ExecuteNonQuery();
        }

        static void InsertFromSelect(SqliteCommand command, string tableName, string[] columns, string select)
        {
            command.CommandText =
            $@"
                INSERT OR IGNORE INTO {tableName} ({string.Join(',', columns)})
                {select}
            ";

            command.ExecuteNonQuery();
        }

        static IEnumerable<string> GetColumns(SqliteConnection connection, string tableName)
        {
            SqliteCommand command = connection.CreateCommand();
            command.CommandText =
            $@"
                PRAGMA table_info({tableName});
            ";

            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Cast<IDataRecord>().Select(dr => dr.GetString(1)).Where(c => c != "Id").ToList();
        }

        static string MakeSelectForInsert(string symbol, IMarketData data)
        {
            //return MakeSelectForInsert_SubSelect(symbol, data);
            return MakeSelectForInsert_LeftJoin(symbol, data);
        }

        static string MakeSelectForInsert_SubSelect(string symbol, IMarketData data)
        {
            string selectPart = null;
            if (data is Bar bar)
            {
                selectPart =
                $@"
                    {(int)bar.BarLength} AS BarLength,
                    (select Id from Bar 
                        where Open = {bar.Open.ToString(CultureInfo.InvariantCulture)} 
                        and Close = {bar.Close.ToString(CultureInfo.InvariantCulture)}
                        and High = {bar.High.ToString(CultureInfo.InvariantCulture)}
                        and Low = {bar.Low.ToString(CultureInfo.InvariantCulture)}
                        and Volume = {bar.Volume}
                     ) as BarId
                ";
            }
            else if (data is BidAsk ba)
            {
                selectPart =
                $@"
                    (select Id from BidAsk 
                        where Bid = {ba.Bid.ToString(CultureInfo.InvariantCulture)} 
                        and BidSize = {ba.BidSize}
                        and Ask = {ba.Ask.ToString(CultureInfo.InvariantCulture)}
                        and AskSize = {ba.AskSize}
                     ) as BidAskId
                ";
            }

            return
            $@"
                SELECT
                (select Id from Stock where Symbol = '{symbol}') as StockId,
                (select Id from Date where Date = '{data.Time.Date.ToShortDateString()}') as DateId,
                (select Id from Time where Time = '{data.Time.TimeOfDay}') as TimeId,
                {selectPart}
            ";
        }

        static string MakeSelectForInsert_LeftJoin(string symbol, IMarketData data)
        {
            string selectPart = null;
            string leftJoinPart = null;
            if (data is Bar bar)
            {
                selectPart =
                $@"
                    {(int)bar.BarLength} AS BarLength,
                    Bar.Id AS BarId   
                ";

                leftJoinPart =
                $@"
                    LEFT JOIN Bar 
                        ON Open = {bar.Open.ToString(CultureInfo.InvariantCulture)}
                        AND Close = {bar.Close.ToString(CultureInfo.InvariantCulture)}
                        AND High = {bar.High.ToString(CultureInfo.InvariantCulture)}
                        AND Low = {bar.Low.ToString(CultureInfo.InvariantCulture)}
                        AND Volume = {bar.Volume}
                ";
            }
            else if (data is BidAsk ba)
            {
                selectPart =
                $@"
                    BidAsk.Id AS BidAskId   
                ";

                leftJoinPart =
                $@"
                    LEFT JOIN BidAsk 
                        ON Bid = {ba.Bid.ToString(CultureInfo.InvariantCulture)}
                        AND BidSize = {ba.BidSize}
                        AND Ask = {ba.Ask.ToString(CultureInfo.InvariantCulture)}
                        AND AskSize = {ba.AskSize}
                ";
            }

            return
            $@"
                SELECT 
                    Stock.Id AS StockId,
                    Date.Id AS DateId,
                    Time.Id AS TimeId,
                    {selectPart}
                FROM Stock
                LEFT JOIN Date ON Date.Date = '{data.Time.Date.ToShortDateString()}'
                LEFT JOIN Time ON Time.Time = '{data.Time.TimeOfDay}'          
                {leftJoinPart}
                WHERE Symbol = '{symbol}'
            ";
        }

        public static IEnumerable<TMarketData> SelectData<TMarketData>(string symbol, DateTime date) where TMarketData : IMarketData, new()
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            string tableName = typeof(TMarketData).Name;
            SqliteCommand command = connection.CreateCommand();
            command.CommandText =
            $@"
                SELECT * FROM Historical{tableName}View
                WHERE Ticker = '{symbol}'
                AND Date = '{date.ToShortDateString()}'
                ORDER BY Time;
            ";

            using SqliteDataReader reader = command.ExecuteReader();

            IEnumerable<TMarketData> data = null;
            if (typeof(TMarketData) == typeof(Bar))
                data = new LinkedList<TMarketData>(reader.Cast<IDataRecord>().Select(dr => MakeBarFromResult(dr)).Cast<TMarketData>());
            else if (typeof(TMarketData) == typeof(BidAsk))
                data = new LinkedList<TMarketData>(reader.Cast<IDataRecord>().Select(dr => MakeBidAskFromResult(dr)).Cast<TMarketData>());

            return data;
        }

        private static Bar MakeBarFromResult(IDataRecord dr)
        {
            DateTime dateTime = DateTime.Parse(dr.GetString(1));
            var bar = new Bar()
            {
                Time = dateTime.AddTicks(TimeSpan.Parse(dr.GetString(2)).Ticks),
                BarLength = (BarLength)dr.GetInt64(3),
                Open = dr.GetDouble(4),
                Close = dr.GetDouble(5),
                High = dr.GetDouble(6),
                Low = dr.GetDouble(7),
            };
            return bar;
        }

        private static BidAsk MakeBidAskFromResult(IDataRecord dr)
        {
            DateTime dateTime = DateTime.Parse(dr.GetString(1));
            var ba = new BidAsk()
            {
                Time = dateTime.AddTicks(TimeSpan.Parse(dr.GetString(2)).Ticks),
                Bid = dr.GetDouble(3),
                BidSize = Convert.ToInt32(dr.GetInt64(4)),
                Ask = dr.GetDouble(5),
                AskSize = Convert.ToInt32(dr.GetInt64(6)),
            };
            return ba;
        }       
    }
}
