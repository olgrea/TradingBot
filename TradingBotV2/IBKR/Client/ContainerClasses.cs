namespace IBApi
{
    public record struct PnL(string Ticker, decimal Pos, double DailyPnL, double UnrealizedPnL, double RealizedPnL, double MarketValue);

    public class Last
    {
        public long Time { get; set; }
        public double Price { get; set; }
        public decimal Size { get; set; }
        public TickAttribLast? TickAttribLast { get; set; }
        public string? Exchange { get; set; }
        public string? SpecialConditions { get; set; }
    }

    public class BidAsk
    {
        public double Bid { get; set; }
        public decimal BidSize { get; set; }
        public double Ask { get; set; }
        public decimal AskSize { get; set; }
        public long Time { get; set; }
        public TickAttribBidAsk? TickAttribBidAsk { get; set; }
    }

    public class FiveSecBar
    {
        public double Open { get; set; }
        public double Close { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public decimal Volume { get; set; }
        public int TradeAmount { get; set; }
        public long Date { get; set; }
        public decimal WAP { get; set; }
    }

    public record struct AccountValue
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public string Currency { get; set; }
        public string AccountName { get; set; }
    }

    public class OrderStatus
    {
        public int OrderId { get; set; }
        public string? Status { get; set; }
        public decimal Filled { get; set; }
        public decimal Remaining { get; set; }
        public double AvgFillPrice { get; set; }
        public int PermId { get; set; }
        public int ParentId { get; set; }
        public double LastFillPrice { get; set; }
        public int ClientId { get; set; }
        public string? WhyHeld { get; set; }
        public double MktCapPrice { get; set; }
    }

    public class Position
    {
        public Position(Contract contract, decimal positionAmount, double averageCost)
        {
            Contract = contract;
            PositionAmount = positionAmount;
            AverageCost = averageCost;
        }

        public Position(Contract contract, decimal positionAmount, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL)
        {
            Contract = contract;
            PositionAmount = positionAmount;
            MarketPrice = marketPrice;
            MarketValue = marketValue;
            AverageCost = averageCost;
            UnrealizedPNL = unrealizedPNL;
            RealizedPNL = realizedPNL;
        }

        public Contract Contract { get; set; }
        public decimal PositionAmount { get; set; }
        public double MarketPrice { get; set; }
        public double MarketValue { get; set; }
        public double AverageCost { get; set; }
        public double UnrealizedPNL { get; set; }
        public double RealizedPNL { get; set; }
    }
}
