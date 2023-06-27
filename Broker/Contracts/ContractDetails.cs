namespace Broker.Contracts
{
    public class ContractDetails
    {
        public Contract? Contract { get; set; }
        public string? MarketName { get; set; }
        public double MinTick { get; set; }
        public string? OrderTypes { get; set; }
        public string? ValidExchanges { get; set; }
        public string? LongName { get; set; }
        public string? Industry { get; set; }
        public string? TimeZoneId { get; set; }
        public string? RegularTradingHours { get; set; }
        public string? StockType { get; set; }

        public static explicit operator IBApi.ContractDetails(ContractDetails cd)
        {
            ArgumentNullException.ThrowIfNull(cd.Contract, nameof(cd.Contract));
            return new IBApi.ContractDetails()
            {
                Contract = (IBApi.Contract)cd.Contract,
                Industry = cd.Industry,
                LongName = cd.LongName,
                MarketName = cd.MarketName,
                MinTick = cd.MinTick,
                OrderTypes = cd.OrderTypes,
                StockType = cd.StockType,
                TimeZoneId = cd.TimeZoneId,
                ValidExchanges = cd.ValidExchanges,
                LiquidHours = cd.RegularTradingHours,
            };
        }

        public static explicit operator ContractDetails(IBApi.ContractDetails cd)
        {
            return new ContractDetails()
            {
                Contract = (Contract)cd.Contract,
                Industry = cd.Industry,
                LongName = cd.LongName,
                MarketName = cd.MarketName,
                MinTick = cd.MinTick,
                OrderTypes = cd.OrderTypes,
                StockType = cd.StockType,
                TimeZoneId = cd.TimeZoneId,
                ValidExchanges = cd.ValidExchanges,
                RegularTradingHours = cd.LiquidHours,
            };
        }
    }
}
