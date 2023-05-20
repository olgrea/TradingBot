using System.Data;
using Microsoft.Data.Sqlite;
using TradingBot.Broker.MarketData;
using TradingBot.Utils;

namespace TradingBot.DataStorage.Sqlite.DbCommands
{
    internal class BidAskExistsCommand : ExistsCommand<BidAsk>
    {
        public BidAskExistsCommand(string symbol, DateRange dateRange, SqliteConnection connection)
            : base(symbol, dateRange, connection)
        {
        }
    }

    internal class SelectBidAsksCommand : SelectCommand<BidAsk>
    {
        public SelectBidAsksCommand(string symbol, DateRange dateRange, SqliteConnection connection)
            : base(symbol, dateRange, connection)
        {
        }

        protected override string MakeSelectCommandText()
        {
            return
            $@"
                SELECT * FROM BidAsksView
                WHERE Ticker = {Sanitize(_symbol)}
                AND Date >= {Sanitize(_dateRange.From.Date)}
                AND Time >= {Sanitize(_dateRange.From.TimeOfDay)}
                AND Date <= {Sanitize(_dateRange.To.Date)}
                AND Time < {Sanitize(_dateRange.To.TimeOfDay)}
                AND Bid IS NOT NULL
                ORDER BY Time;
            ";
        }

        protected override BidAsk MakeDataFromResults(IDataRecord dr)
        {
            DateTime dateTime = DateTime.Parse(dr.GetString(1));
            var ba = new BidAsk()
            {
                Time = dateTime.AddTicks(TimeSpan.Parse(dr.GetString(2)).Ticks),
                Bid = dr.GetDouble(3),
                BidSize = Convert.ToDecimal(dr.GetDouble(4)),
                Ask = dr.GetDouble(5),
                AskSize = Convert.ToDecimal(dr.GetDouble(6)),
            };
            return ba;
        }
    }

    internal class InsertBidAsksCommand : InsertCommand<BidAsk>
    {
        public InsertBidAsksCommand(string symbol, DateRange dateRange, IEnumerable<BidAsk> dataCollection, SqliteConnection connection)
            : base(symbol, dateRange, dataCollection, connection)
        {
        }

        protected override int InsertMarketData(SqliteCommand command, BidAsk data)
        {
            var columns = new string[] { "Bid", "BidSize", "Ask", "AskSize" };
            var values = new object[] { data.Bid, data.BidSize, data.Ask, data.AskSize };
            Insert(command, "BidAskPrices", columns, values);
            return InsertFromSelect(command, data);
        }

        protected int InsertFromSelect(SqliteCommand command, BidAsk data)
        {
            command.CommandText =
            $@"
                INSERT OR IGNORE INTO BidAsks (Ticker, DateTime, BidAsk)
                SELECT 
                    Tickers.Id,
                    {Sanitize(data.Time.ToUnixTimeSeconds())},
                    BidAskPrices.Id
                FROM BidAskPrices
                LEFT JOIN Tickers ON Symbol = {Sanitize(_symbol)}  
                WHERE Bid = {Sanitize(data.Bid)}
                AND BidSize = {Sanitize(data.BidSize)}
                AND Ask = {Sanitize(data.Ask)}
                AND AskSize = {Sanitize(data.AskSize)}
            ";

            return command.ExecuteNonQuery();
        }

        protected override int InsertNullMarketData(SqliteCommand command, DateTime dateTime)
        {
            command.CommandText =
            $@"
                INSERT OR IGNORE INTO BidAsks (Ticker, DateTime, BidAsk)
                SELECT 
                    Tickers.Id,
                    {Sanitize(dateTime.ToUnixTimeSeconds())},
                    NULL
                FROM Tickers WHERE Symbol = {Sanitize(_symbol)} 
            ";

            return command.ExecuteNonQuery();
        }
    }
}
