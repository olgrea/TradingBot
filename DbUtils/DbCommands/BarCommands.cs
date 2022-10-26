using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using NLog.Fluent;
using TradingBot.Broker.MarketData;

namespace DbUtils.DbCommands
{
    internal class BarExistsCommand : ExistsCommand<Bar>
    {
        BarLength _barLength;

        public BarExistsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange, BarLength barLength, SqliteConnection connection) 
            : base(symbol, date, timeRange, connection)
        {
            _barLength = barLength;
        }

        protected override string MakeExistsCommandText()
        {
            return
            $@"
                SELECT EXISTS (
                    SELECT 1 FROM HistoricalBarView
                        WHERE Ticker = {Sanitize(_symbol)}
                        AND Date = {Sanitize(_date.Date)}
                        AND Time >= {Sanitize(_timeRange.Item1)} 
                        AND Time < {Sanitize(_timeRange.Item2)}
                        AND BarLength = {Sanitize(_barLength)}
                );
            ";
        }
    }

    internal class SelectBarsCommand : SelectCommand<Bar>
    {
        BarLength _barLength;

        public SelectBarsCommand(string symbol, DateTime date, (TimeSpan, TimeSpan) timeRange, BarLength barLength, SqliteConnection connection)
            : base(symbol, date, timeRange, connection)
        {
            _barLength = barLength;
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
            };
            return bar;
        }

        protected override string MakeSelectCommandText()
        {
            return
            $@"
                SELECT * FROM Historical{_marketDataName}View
                WHERE Ticker = {Sanitize(_symbol)}
                AND Date = {Sanitize(_date)}
                AND Time >= {Sanitize(_timeRange.Item1)}
                AND Time < {Sanitize(_timeRange.Item2)}
                AND BarLength = {Sanitize(_barLength)}
                ORDER BY Time;
            ";
        }
    }

    internal class InsertBarsCommand : InsertCommand<Bar>
    {
        public InsertBarsCommand(string symbol, IEnumerable<Bar> dataCollection, SqliteConnection connection)
            : base(symbol, dataCollection, connection)
        {
        }

        protected override void InsertMarketData(SqliteCommand command, Bar data)
        {
            Insert(command, "Date", "Date", data.Time.Date.ToShortDateString());
            Insert(command, "Time", "Time", data.Time.TimeOfDay.ToString());

            var columns = new string[] { "Open", "Close", "High", "Low", "Volume" };
            var values = new object[] { data.Open, data.Close, data.High, data.Low, data.Volume };
            Insert(command, "Bar", columns, values);

            InsertFromSelect(command, data);
        }

        protected int InsertFromSelect(SqliteCommand command, Bar data)
        {
            command.CommandText =
            $@"
                INSERT OR IGNORE INTO HistoricalBar (Stock, Date, Time, BarLength, Bar)
                SELECT 
                    Stock.Id AS StockId,
                    Date.Id AS DateId,
                    Time.Id AS TimeId,
                    {Sanitize(data.BarLength)} AS BarLength,
                    Bar.Id AS BarId   
                FROM Stock
                LEFT JOIN Date ON Date.Date = {Sanitize(data.Time.Date)}
                LEFT JOIN Time ON Time.Time = {Sanitize(data.Time.TimeOfDay)}
                LEFT JOIN Bar 
                    ON Open = {Sanitize(data.Open)}
                    AND Close = {Sanitize(data.Close)}
                    AND High = {Sanitize(data.High)}
                    AND Low = {Sanitize(data.Low)}
                    AND Volume = {Sanitize(data.Volume)}
                WHERE Symbol = {Sanitize(_symbol)}
            ";

            return command.ExecuteNonQuery();
        }
    }
}
