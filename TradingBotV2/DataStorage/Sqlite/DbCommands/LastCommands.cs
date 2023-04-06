using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using TradingBotV2.Broker.MarketData;

namespace TradingBotV2.DataStorage.Sqlite.DbCommands
{
    internal class LastExistsCommand : ExistsCommand<Last>
    {
        public LastExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange, SqliteConnection connection)
            : base(symbol, date, timeRange, connection)
        {
        }
    }

    internal class SelectLastsCommand : SelectCommand<Last>
    {
        public SelectLastsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange, SqliteConnection connection)
            : base(symbol, date, timeRange, connection)
        {
        }

        protected override Last MakeDataFromResults(IDataRecord dr)
        {
            DateTime dateTime = DateTime.Parse(dr.GetString(1));
            var last = new Last()
            {
                Time = dateTime.AddTicks(TimeSpan.Parse(dr.GetString(2)).Ticks),
                Price = dr.GetDouble(3),
                Size = Convert.ToInt32(dr.GetInt64(4)),
            };
            return last;
        }
    }

    internal class InsertLastsCommand : InsertCommand<Last>
    {
        public InsertLastsCommand(string symbol, IEnumerable<Last> dataCollection, SqliteConnection connection)
            : base(symbol, dataCollection, connection)
        {
        }

        protected override void InsertMarketData(SqliteCommand command, Last data)
        {
            var columns = new string[] { "Price", "Size" };
            var values = new object[] { data.Price, data.Size };
            Insert(command, "Last", columns, values);
            InsertFromSelect(command, data);
        }

        protected void InsertFromSelect(SqliteCommand command, Last data)
        {
            command.CommandText =
            $@"
                INSERT OR IGNORE INTO HistoricalLast (Stock, DateTime, Last)
                SELECT 
                    Stock.Id AS StockId,
                    {Sanitize(new DateTimeOffset(data.Time).ToUnixTimeSeconds())} AS DateTime,                                    
                    Last.Id AS LastId   
                FROM Last
                LEFT JOIN Stock ON Symbol = {Sanitize(_symbol)} 
                WHERE Price = {Sanitize(data.Price)}
                AND Size = {Sanitize(data.Size)}
            ";

            command.ExecuteNonQuery();
        }
    }
}
