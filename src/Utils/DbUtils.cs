// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Data;
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
                    SELECT 1 FROM {tableName}
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

        public static void InsertData<TMarketData>(string symbol, IEnumerable<TMarketData> data, SqliteConnection connection) where TMarketData : IMarketData, new()
        {
            using var transaction = connection.BeginTransaction();

            SqliteCommand command = CreateInsertCommand<TMarketData>(connection);

            command.Parameters["$Ticker"].Value = symbol;
            foreach (TMarketData d in data)
            {
                PopulateCommandParams(command.Parameters, d);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        public static IEnumerable<TMarketData> SelectData<TMarketData>(string symbol, DateTime date) where TMarketData : IMarketData, new()
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            string tableName = typeof(TMarketData).Name;
            SqliteCommand command = connection.CreateCommand();
            command.CommandText =
            $@"
                SELECT * FROM {tableName}
                WHERE Ticker = '{symbol}'
                AND Date = '{date.ToShortDateString()}';
            ";

            var data = new LinkedList<TMarketData>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var name = reader.GetString(0);
                }
            }

            return data;
        }

        //IMarketData ConvertResultToPopulate

        static SqliteCommand CreateInsertCommand<TMarketData>(SqliteConnection connection) where TMarketData : IMarketData, new()
        {
            string tableName = typeof(TMarketData).Name;
            IEnumerable<string> columns = GetColumns<TMarketData>(connection);
            IEnumerable<string> values = columns.Select(s => "$" + s);

            SqliteCommand readerCommand = connection.CreateCommand();

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

        static IEnumerable<string> GetColumns<TMarketData>(SqliteConnection connection)
        {
            SqliteCommand command = connection.CreateCommand();
            command.CommandText =
            $@"
                PRAGMA table_info({typeof(TMarketData).Name});
            ";

            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Cast<IDataRecord>().Select(dr => dr.GetString(1)).ToList();
        }

        static void PopulateCommandParams<TMarketData>(SqliteParameterCollection parameters, TMarketData data) where TMarketData : IMarketData, new()
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
