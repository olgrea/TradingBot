namespace InteractiveBrokers.Contracts
{
    public class ContractDetails
    {
        public Contract Contract { get; set; }
        public string MarketName { get; set; }
        public double MinTick { get; set; }
        public string OrderTypes { get; set; }
        public string ValidExchanges { get; set; }
        public string LongName { get; set; }
        public string Industry { get; set; }
        public string TimeZoneId { get; set; }
        public string RegularTradingHours { get; set; }
        public string StockType { get; set; }

    }
}
