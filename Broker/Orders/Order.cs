using System;
using System.Collections.Generic;

namespace TradingBot.Broker.Orders
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

    public class RequestInfo
    {
        public int OrderId { get; set; }
        public int ClientId { get; set; }
        public int ParentId { get; set; }
        public int PermId { get; set; }
        public bool Transmit { get; set; } // if false, order will be created but not transmitted
        public string OcaGroup { get; set; }
        public OcaType OcaType { get; set; }
    }

    public abstract class Order
    {
        public RequestInfo RequestInfo { get; } = new RequestInfo();
        public int Id
        {
            get => RequestInfo.OrderId;
            set => RequestInfo.OrderId = value;
        }

        public readonly string OrderType;
        protected Order(string orderType) => OrderType = orderType;
        public OrderAction Action { get; set; }
        public double TotalQuantity { get; set; }
    }

    public class MarketOrder : Order
    {
        public MarketOrder() : base("MKT") { }
    }

    public class MarketIfTouchedOrder : Order
    {
        public MarketIfTouchedOrder() : base("MIT") { }
        public double TouchPrice { get; set; }
    }

    public class LimitOrder : Order
    {
        public LimitOrder() : base("LMT") { }
        public double LmtPrice { get; set; }
    }

    public class StopOrder : Order
    {
        public StopOrder() : base("STP") { }
        public double StopPrice { get; set; }
    }

    public class TrailingStopOrder : Order
    {
        public TrailingStopOrder() : base("TRAIL") { }
        public double StopPrice { get; set; }

        // Ignored if StopPrice is set
        public double TrailingPercent { get; set; }
    }
}
