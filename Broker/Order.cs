using System;
using System.Collections.Generic;
using System.Text;
using IBApi;

namespace TradingBot.Broker
{
    public enum OrderAction
    {
        BUY, SELL
    }

    public enum OcaType
    {
        NONE = 0,
        CANCEL_WITH_BLOCK = 1,
        REDUCE_WITH_BLOCK = 2,
        REDUCE_NON_BLOCK = 3,
    }

    public abstract class Order
    {
        public readonly string OrderType;
        protected Order(string orderType) => OrderType = orderType;

        public int ClientId { get; set; }
        public OrderAction Action { get; set; }
        public double TotalQuantity { get; set; }

        public string OcaGroup { get; set; }
        public OcaType OcaType { get; set; }
        
        // if false, order will be created but not transmitted
        public bool Transmit { get; set; }
        
        public int ParentId { get; set; }
    }

    public class Market : Order 
    {
        public Market() : base("MKT") { }
    }

    public class MarketIfTouched : Order
    {
        public MarketIfTouched() : base("MIT") { }
        public Decimal TouchPrice { get; set; }
    }
    
    public class Limit : Order 
    {
        public Limit() : base("LMT") { }
        public Decimal LmtPrice { get; set; }
    }

    public class Stop : Order
    {
        public Stop() : base("STP") { }
        public Decimal StopPrice { get; set; }
    }

    public class TrailingStop : Order
    {
        public TrailingStop() : base("TRAIL") { }
        public Decimal StopPrice { get; set; }

        // Ignored if StopPrice is set
        public double TrailingPercent { get; set; }
    }
}
