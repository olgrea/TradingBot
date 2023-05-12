using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using TradingBotV2.Broker.MarketData;
using TradingBotV2.Utils;
using static TradingBotV2.DataStorage.Sqlite.DbCommandFactories.DbCommandFactory;

namespace TradingBotV2.DataStorage.Sqlite.DbCommands
{
    internal class LastExistsCommand : ExistsCommand<Last>
    {
        public LastExistsCommand(string symbol, DateRange dateRange, SqliteConnection connection)
            : base(symbol, dateRange, connection)
        {
        }
    }

    internal class SelectLastsCommand : SelectCommand<Last>
    {
        public SelectLastsCommand(string symbol, DateRange dateRange, SqliteConnection connection) 
            : base(symbol, dateRange, connection)
        {
        }

        protected override string MakeSelectCommandText()
        {
            return
            $@"
                SELECT * FROM LastsView
                WHERE Ticker = {Sanitize(_symbol)}
                AND Date >= {Sanitize(_dateRange.From.Date)}
                AND Time >= {Sanitize(_dateRange.From.TimeOfDay)}
                AND Date <= {Sanitize(_dateRange.To.Date)}
                AND Time < {Sanitize(_dateRange.To.TimeOfDay)}
                AND Price IS NOT NULL
                ORDER BY Time;
            ";
        }

        protected override Last MakeDataFromResults(IDataRecord dr)
        {
            DateTime dateTime = DateTime.Parse(dr.GetString(1));
            var last = new Last()
            {
                Time = dateTime.AddTicks(TimeSpan.Parse(dr.GetString(2)).Ticks),
                Price = dr.GetDouble(3),
                Size = dr.GetDecimal(4),
            };
            return last;
        }
    }

    internal class InsertLastsCommand : InsertCommand<Last>
    {
        public InsertLastsCommand(string symbol, DateRange dateRange, IEnumerable<Last> dataCollection, SqliteConnection connection)
            : base(symbol, dateRange, dataCollection, connection)
        {
        }

        protected override int InsertMarketData(SqliteCommand command, Last data)
        {
            command.CommandText =
            $@"
                INSERT OR IGNORE INTO Lasts (Ticker, DateTime, Price, Size)
                VALUES (
                    ( SELECT Tickers.Id From Tickers
                    WHERE Symbol = {Sanitize(_symbol)} ),
                    {Sanitize(data.Time.ToUnixTimeSeconds())},
                    {Sanitize(data.Price)},
                    {Sanitize(data.Size)}
                )
            ";

            return command.ExecuteNonQuery();
        }

        protected override int InsertNullMarketData(SqliteCommand command, DateTime dateTime)
        {
            command.CommandText =
            $@"
                INSERT OR IGNORE INTO Lasts (Ticker, DateTime, Price, Size)
                VALUES (
                    ( SELECT Tickers.Id From Tickers
                    WHERE Symbol = {Sanitize(_symbol)} ),
                    {Sanitize(dateTime.ToUnixTimeSeconds())},
                    NULL,
                    NULL
                )
            ";

            return command.ExecuteNonQuery();
        }
    }
}
