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

        // TODO : handle order id better?
        public int ClientId { get; set; }

        public OrderAction Action { get; set; }
        public double TotalQuantity { get; set; }

        public string OcaGroup { get; set; }
        public OcaType OcaType { get; set; }

        // if false, order will be created but not transmitted
        public bool Transmit { get; set; } = true;
        
        public int ParentId { get; set; }

        // TODO : handle attached order better and test this
        //public List<Order> Children { get; set; } = new List<Order>();
    }

    public class MarketOrder : Order 
    {
        public MarketOrder() : base("MKT") { }
    }

    public class MarketIfTouchedOrder : Order
    {
        public MarketIfTouchedOrder() : base("MIT") { }
        public Decimal TouchPrice { get; set; }
    }
    
    public class LimitOrder : Order 
    {
        public LimitOrder() : base("LMT") { }
        public Decimal LmtPrice { get; set; }
    }

    public class StopOrder : Order
    {
        public StopOrder() : base("STP") { }
        public Decimal StopPrice { get; set; }
    }

    public class TrailingStopOrder : Order
    {
        public TrailingStopOrder() : base("TRAIL") { }
        public Decimal StopPrice { get; set; }

        // Ignored if StopPrice is set
        public double TrailingPercent { get; set; }
    }
}
