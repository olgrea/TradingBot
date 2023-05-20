--
-- File generated with SQLiteStudio v3.3.3 on dim. mai 14 09:15:20 2023
--
-- Text encoding used: System
--
PRAGMA foreign_keys = off;
BEGIN TRANSACTION;

-- Table: Bars
CREATE TABLE Bars (Id INTEGER PRIMARY KEY NOT NULL, Ticker REFERENCES Tickers (Id) ON DELETE CASCADE ON UPDATE CASCADE NOT NULL, DateTime INTEGER NOT NULL, BarLength INTEGER NOT NULL, OHLC REFERENCES OHLC (Id) ON DELETE RESTRICT ON UPDATE RESTRICT, Volume REAL, WAP REAL, NbTrades INTEGER, UNIQUE (Ticker, DateTime, BarLength));

-- Table: BidAskPrices
CREATE TABLE BidAskPrices (Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, Bid REAL (4, 2) NOT NULL, BidSize REAL NOT NULL, Ask REAL (4, 2) NOT NULL, AskSize REAL NOT NULL, UNIQUE (Bid, BidSize, Ask, AskSize) ON CONFLICT IGNORE);

-- Table: BidAsks
CREATE TABLE BidAsks (Id INTEGER PRIMARY KEY NOT NULL, Ticker REFERENCES Tickers (Id) ON DELETE CASCADE ON UPDATE CASCADE NOT NULL, DateTime INTEGER NOT NULL, BidAsk REFERENCES BidAskPrices (Id) ON DELETE RESTRICT ON UPDATE RESTRICT, UNIQUE (Ticker, DateTime));

-- Table: Lasts
CREATE TABLE Lasts (Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, Ticker REFERENCES Tickers (Id) ON DELETE CASCADE ON UPDATE CASCADE NOT NULL, DateTime INTEGER NOT NULL, Price REAL (4, 2), Size REAL, UNIQUE (Ticker, DateTime));

-- Table: OHLC
CREATE TABLE OHLC (Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, Open REAL (4, 2) NOT NULL, Close REAL (4, 2) NOT NULL, High REAL (4, 2) NOT NULL, Low REAL (4, 2) NOT NULL, UNIQUE (Open, Close, High, Low) ON CONFLICT IGNORE);

-- Table: Tickers
CREATE TABLE Tickers (Id INTEGER PRIMARY KEY AUTOINCREMENT, Symbol TEXT NOT NULL UNIQUE);

-- View: BarsView
CREATE VIEW BarsView AS SELECT Tickers.Symbol AS Ticker, 
date(DateTime, 'unixepoch', 'localtime') AS Date, 
time(DateTime, 'unixepoch', 'localtime') AS Time, 
BarLength, 
OHLC.Open, 
OHLC.Close, 
OHLC.High, 
OHLC.Low, 
Bars.Volume,
Bars.WAP,
Bars.NbTrades
FROM Bars 
LEFT JOIN Tickers ON Tickers.Id = Bars.Ticker 
LEFT JOIN OHLC ON OHLC.Id = Bars.OHLC
ORDER BY DateTime;

-- View: BidAsksView
CREATE VIEW BidAsksView AS SELECT Tickers.Symbol AS Ticker, 
date(DateTime, 'unixepoch', 'localtime') AS Date, 
time(DateTime, 'unixepoch', 'localtime') AS Time, 
BidAskPrices.Bid,
BidAskPrices.BidSize,
BidAskPrices.Ask,
BidAskPrices.AskSize
FROM BidAsks 
LEFT JOIN Tickers ON Tickers.Id = BidAsks.Ticker 
LEFT JOIN BidAskPrices ON BidAskPrices.Id = BidAsks.BidAsk
ORDER BY DateTime;

-- View: LastsView
CREATE VIEW LastsView AS SELECT Tickers.Symbol AS Ticker, 
date(DateTime, 'unixepoch', 'localtime') AS Date, 
time(DateTime, 'unixepoch', 'localtime') AS Time, 
Lasts.Price,
Lasts.Size
FROM Lasts
LEFT JOIN Tickers ON Tickers.Id = Lasts.Ticker
ORDER BY DateTime;

COMMIT TRANSACTION;
PRAGMA foreign_keys = on;
