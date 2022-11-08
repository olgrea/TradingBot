using System;
using System.Collections.Generic;
using System.Data;
using InteractiveBrokers.MarketData;
using Microsoft.Data.Sqlite;

namespace DataStorage.Db.DbCommands
{
    internal class BidAskExistsCommand : ExistsCommand<BidAsk>
    {
        public BidAskExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange, SqliteConnection connection)
            : base(symbol, date, timeRange, connection)
        {
        }
    }

    internal class SelectBidAsksCommand : SelectCommand<BidAsk>
    {
        public SelectBidAsksCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange, SqliteConnection connection)
            : base(symbol, date, timeRange, connection)
        {
        }

        protected override BidAsk MakeDataFromResults(IDataRecord dr)
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

    internal class InsertBidAsksCommand : InsertCommand<BidAsk>
    {
        public InsertBidAsksCommand(string symbol, IEnumerable<BidAsk> dataCollection, SqliteConnection connection)
            : base(symbol, dataCollection, connection)
        {
        }

        protected override void InsertMarketData(SqliteCommand command, BidAsk data)
        {
            var columns = new string[] { "Bid", "BidSize", "Ask", "AskSize" };
            var values = new object[] { data.Bid, data.BidSize, data.Ask, data.AskSize };
            Insert(command, "BidAsk", columns, values);
            InsertFromSelect(command, data);
        }

        protected void InsertFromSelect(SqliteCommand command, BidAsk data)
        {
            command.CommandText =
            $@"
                INSERT OR IGNORE INTO HistoricalBidAsk (Stock, DateTime, BidAsk)
                SELECT 
                    Stock.Id AS StockId,
                    {Sanitize(new DateTimeOffset(data.Time).ToUnixTimeSeconds())} AS DateTime,                                    
                    BidAsk.Id AS BidAskId   
                FROM BidAsk
                LEFT JOIN Stock ON Symbol = {Sanitize(_symbol)} 
                WHERE Bid = {Sanitize(data.Bid)}
                AND BidSize = {Sanitize(data.BidSize)}
                AND Ask = {Sanitize(data.Ask)}
                AND AskSize = {Sanitize(data.AskSize)}
            ";

            command.ExecuteNonQuery();
        }
    }
}
