using System.Globalization;

namespace TradingBotV2.Broker.Contracts
{
    public enum OptionType
    {
        Call, Put
    }

    public abstract class Contract
    {
        public int Id { get; set; }
        public string? Symbol { get; set; }
        public string? SecType { get; set; }
        public string? Exchange { get; set; }
        public string? Currency { get; set; }

        public override int GetHashCode() => Id;
        public override bool Equals(object? obj)
        {
            var c = obj as Contract;
            return Id.Equals(c?.Id);
        }

        public static explicit operator Contract(IBApi.Contract ibc)
        {
            switch (ibc.SecType)
            {
                case "STK":
                    return (Stock)ibc;

                case "OPT":
                    return (Option)ibc;

                case "CASH":
                    return (Cash)ibc;

                default:
                    throw new NotSupportedException($"This type of contract is not supported : {ibc.SecType}");
            }
        }

        public static explicit operator IBApi.Contract(Contract c)
        {
            if (c is Cash cash)
                return (IBApi.Contract)cash;
            else if (c is Stock stock)
                return (IBApi.Contract)stock;
            else if (c is Option opt)
                return (IBApi.Contract)opt;
            else
                throw new NotSupportedException($"This type of contract is not supported : {c.SecType}");
        }
    }

    public class Cash : Contract
    {
        public Cash() => SecType = "CASH";
        public override string? ToString()
        {
            return Currency;
        }

        public static explicit operator Cash(IBApi.Contract ibc)
        {
            return new Cash()
            {
                Id = ibc.ConId,
                Currency = ibc.Currency,
                Exchange = ibc.Exchange ?? ibc.PrimaryExch,
                Symbol = ibc.Symbol,
            };
        }

        public static explicit operator IBApi.Contract(Cash c)
        {
            return new IBApi.Contract()
            {
                ConId = c.Id,
                Currency = c.Currency,
                SecType = c.SecType,
                Symbol = c.Symbol,
                Exchange = c.Exchange,
            };
        }
    }

    public class Stock : Contract
    {
        public Stock() => SecType = "STK";
        public string? LastTradeDate { get; set; }

        public override string? ToString()
        {
            return Symbol;
        }

        public static explicit operator Stock(IBApi.Contract ibc)
        {
            return new Stock()
            {
                Id = ibc.ConId,
                Currency = ibc.Currency,
                Exchange = ibc.Exchange ?? ibc.PrimaryExch,
                Symbol = ibc.Symbol,
                LastTradeDate = ibc.LastTradeDateOrContractMonth,
            };
        }

        public static explicit operator IBApi.Contract(Stock c)
        {
            return new IBApi.Contract()
            {
                ConId = c.Id,
                Currency = c.Currency,
                SecType = c.SecType,
                Symbol = c.Symbol,
                Exchange = c.Exchange,
                LastTradeDateOrContractMonth = c.LastTradeDate,
            };
        }
    }

    //TODO : add proper options support
    public class Option : Contract
    {
        public Option() => SecType = "OPT";
        public double Strike { get; set; }
        public double Multiplier { get; set; }
        public string? ContractMonth { get; set; }
        public OptionType OptionType { get; set; }

        public override string ToString()
        {
            return $"{Symbol} {ContractMonth} {Strike} {OptionType}";
        }

        public static explicit operator Option(IBApi.Contract ibc)
        {
            return new Option()
            {
                Id = ibc.ConId,
                Currency = ibc.Currency,
                Exchange = ibc.Exchange ?? ibc.PrimaryExch,
                Symbol = ibc.Symbol,
                ContractMonth = ibc.LastTradeDateOrContractMonth,
                Strike = ibc.Strike,
                Multiplier = double.Parse(ibc.Multiplier, CultureInfo.InvariantCulture),
                OptionType = ibc.Right == "C" || ibc.Right == "CALL" ? OptionType.Call : OptionType.Put,
            };
        }

        public static explicit operator IBApi.Contract(Option c)
        {
            return new IBApi.Contract()
            {
                ConId = c.Id,
                Currency = c.Currency,
                SecType = c.SecType,
                Symbol = c.Symbol,
                Exchange = c.Exchange,
                LastTradeDateOrContractMonth = c.ContractMonth,
                Strike = c.Strike,
                Multiplier = c.Multiplier.ToString(),
                Right = c.OptionType.ToString()[0].ToString(),
            };
        }
    }
}
