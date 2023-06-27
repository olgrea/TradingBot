using System.Data;
using System.Diagnostics;
using Broker.MarketData;
using Broker.Utils;
using Microsoft.Data.Sqlite;

namespace Broker.DataStorage.Sqlite.DbCommands
{
    internal class BarExistsCommand : ExistsCommand<Bar>
    {
        BarLength _barLength;

        public BarExistsCommand(string symbol, DateRange dateRange, BarLength barLength, SqliteConnection connection)
            : base(symbol, dateRange, connection)
        {
            _barLength = barLength;
        }

        protected override string MakeExistsCommandText()
        {
            var nbTimestamps = (_dateRange.To - _dateRange.From).TotalSeconds;
            return
            $@"
                SELECT EXISTS (
                    SELECT 1 WHERE 
                        (SELECT COUNT(DISTINCT DateTime) FROM {_marketDataName}s
                            LEFT JOIN Tickers ON Tickers.Id = {_marketDataName}s.Ticker
                            WHERE Symbol = {Sanitize(_symbol)}
                            AND BarLength = {Sanitize(_barLength)}
                            AND DateTime >= {Sanitize(_dateRange.From.ToUnixTimeSeconds())} 
                            AND DateTime < {Sanitize(_dateRange.To.ToUnixTimeSeconds())} 
                        ) = {Sanitize(nbTimestamps)}
                );
            ";
        }
    }

    internal class SelectBarsCommand : SelectCommand<Bar>
    {
        BarLength _barLength;

        public SelectBarsCommand(string symbol, DateRange dateRange, BarLength barLength, SqliteConnection connection)
            : base(symbol, dateRange, connection)
        {
            _barLength = barLength;
        }

        protected override string MakeSelectCommandText()
        {
            return
            $@"
                SELECT * FROM BarsView
                WHERE Ticker = {Sanitize(_symbol)}
                AND Date >= {Sanitize(_dateRange.From.Date)}
                AND Time >= {Sanitize(_dateRange.From.TimeOfDay)}
                AND Date <= {Sanitize(_dateRange.To.Date)}
                AND Time < {Sanitize(_dateRange.To.TimeOfDay)}
                AND Open IS NOT NULL
                ORDER BY Time;
            ";
        }

        protected override Bar MakeDataFromResults(IDataRecord dr)
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
                Volume = Convert.ToDecimal(dr.GetDouble(8)),
                WAP = Convert.ToDecimal(dr.GetDouble(9)),
                NbTrades = Convert.ToInt32(dr.GetInt64(10)),
            };
            return bar;
        }
    }

    internal class InsertBarsCommand : InsertCommand<Bar>
    {
        BarLength _barLength;
        public InsertBarsCommand(string symbol, DateRange dateRange, IEnumerable<Bar> dataCollection, SqliteConnection connection)
            : base(symbol, dateRange, dataCollection, connection)
        {
            _barLength = dataCollection.First().BarLength;
            if (dataCollection.Any(d => d.BarLength != _barLength))
                throw new NotSupportedException("Bars insertions with different bar lenghts not supported");
        }

        protected override int InsertMarketData(SqliteCommand command, Bar data)
        {
            var columns = new string[] { "Open", "Close", "High", "Low" };
            var values = new object[] { data.Open, data.Close, data.High, data.Low };
            Insert(command, "OHLC", columns, values);
            return InsertFromSelect(command, data);
        }

        protected int InsertFromSelect(SqliteCommand command, Bar data)
        {
            command.CommandText =
            $@"
                INSERT OR IGNORE INTO Bars (Ticker, DateTime, BarLength, OHLC, Volume, WAP, NbTrades)
                SELECT 
                    Tickers.Id,
                    {Sanitize(data.Time.ToUnixTimeSeconds())},
                    {Sanitize(data.BarLength)},
                    OHLC.Id,
                    {Sanitize(data.Volume)},
                    {Sanitize(data.WAP)},
                    {Sanitize(data.NbTrades)}
                FROM OHLC
                LEFT JOIN Tickers ON Symbol = {Sanitize(_symbol)}  
                WHERE Open = {Sanitize(data.Open)}
                AND Close = {Sanitize(data.Close)}
                AND High = {Sanitize(data.High)}
                AND Low = {Sanitize(data.Low)}
            ";

            return command.ExecuteNonQuery();
        }

        protected override int InsertNullMarketData(SqliteCommand command, DateTime dateTime)
        {
            command.CommandText =
            $@"
                INSERT OR IGNORE INTO Bars (Ticker, DateTime, BarLength, OHLC, Volume, WAP, NbTrades)
                SELECT 
                    Tickers.Id,
                    {Sanitize(dateTime.ToUnixTimeSeconds())},
                    {Sanitize(_barLength)},
                    NULL, 
                    NULL, 
                    NULL, 
                    NULL
                FROM Tickers WHERE Symbol = {Sanitize(_symbol)} 
            ";

            return command.ExecuteNonQuery();
        }
    }
}
