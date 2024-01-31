# High-frequency Trading bot
**Note : This project is in early alpha stage, has mostly been put on hold and was never meant to be shared publicly.** 

**I am temporarily making it public in order for potential recruiters to see the work I've done during my career hiatus.**

My attempt at making a high-frequency trading bot. It uses the [Trader Workstation C# API](https://interactivebrokers.github.io/tws-api/index.html) made by [Interactive Brokers](https://www.interactivebrokers.ca/en/home.php). 

To be able to compile and run the code of this repository, you need to :

- have an Interactive Broker account
- have a valid market data subscription (paid)
- install [Trader Workstation](https://www.interactivebrokers.ca/en/trading/tws.php#tws-software) or [IB Gateway](https://www.interactivebrokers.ca/en/trading/ibgateway-latest.php).
- install the [TWS API](https://interactivebrokers.github.io/).

As of now, this project contains five major components :

## IBBroker

My own implementation of Interactive Broker's API 

The event-based model used by the API has been wrapped and converted to the async/await model.

As of now, only contracts of type Stocks are supported.

## IBHistoricalDataProvider
A module that retrieves historical market data from Interactive Broker's servers while respecting API limitations described [here](https://interactivebrokers.github.io/tws-api/historical_limitations.html).

IB is not a specialised market data provider and has therefore put restrictions in place to limit traffic which is not directly associated to trading. 

A Pacing Violation occurs whenever one or more of the following restrictions is not observed:

- Making identical historical data requests within 15 seconds.
- Making six or more historical data requests for the same Contract, Exchange and Tick Type within two seconds.
- Making more than 60 requests within any ten minute period.

The data retrieved is stored in an sqlite database.

An instance of this class can return one second candles, Bid/Ask data and Last Price data for a specified stock available on the NYSE.

## Backtester
Used to backtest a trading strategy against a specific period of time. 

Basically, I reverse-engineered the Trader Workstation app's handling of orders. 

## Trader
Handles signals received from a strategy by sending out the appropriate buy or sell order to a broker.

## Strategy
The actual strategy used to decide if a stock should be bought or sold. 

This part of the code is located in a separate private project and will not be shared publicly.