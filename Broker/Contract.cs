using System;
using System.Collections.Generic;
using System.Text;
using IBApi;

namespace TradingBot.Broker
{
    public enum OptionType
    {
        Call, Put
    }

    public abstract class Contract
    {
        public int Id { get; set; } = -1;
        public string Symbol { get; set; }
        public string SecType { get; set; }
        public string Exchange { get; set; }
        public string Currency { get; set; }

        public override int GetHashCode() => Id;
        public override bool Equals(object obj)
        {
            var c = obj as Contract;
            return Id.Equals(c);
        }
    }

    public class Cash : Contract
    {
        public Cash() => SecType = "CASH";
    }

    public class Stock : Contract
    {
        public Stock() => SecType = "STK";
        public string LastTradeDate { get; set; }

        public override string ToString()
        {
            return Symbol;
        }
    }

    public class Option : Contract
    {
        public Option() => SecType = "OPT";
        public double Strike { get; set; }
        public double Multiplier { get; set; }
        public string ContractMonth { get; set; }
        public OptionType OptionType { get; set; }

        public override string ToString()
        {
            return $"{Symbol} {ContractMonth} {Strike} {OptionType}";
        }
    }
}
