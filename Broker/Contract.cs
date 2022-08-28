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
        public string Exchange { get; set; }
        public string Currency { get; set; }

        public override int GetHashCode() => Id;
    }

    internal class Stock : Contract
    {
        public string LastTradeDate { get; set; }

        public override string ToString()
        {
            return Symbol;
        }
    }

    internal class Option : Contract
    {
        public Decimal Strike { get; set; }
        public Decimal Multiplier { get; set; }
        public string ContractMonth { get; set; }
        public OptionType OptionType { get; set; }

        public override string ToString()
        {
            return $"{Symbol} {ContractMonth} {Strike} {OptionType}";
        }
    }
}
