using System;
using System.Collections.Generic;
using System.Diagnostics;
using TradingBot.Broker.MarketData;

namespace TradingBot.Broker.Orders
{
    internal enum OrderAction
    {
        BUY, SELL
    }

    internal enum OcaType
    {
        NONE = 0,
        CANCEL_WITH_BLOCK = 1,
        REDUCE_WITH_BLOCK = 2,
        REDUCE_NON_BLOCK = 3,
    }

    internal class RequestInfo
    {
        public int OrderId { get; set; }
        public int ClientId { get; set; }
        public int ParentId { get; set; }
        public int PermId { get; set; }
        public bool Transmit { get; set; } = true; // if false, order will be created but not transmitted
        public string OcaGroup { get; set; }
        public OcaType OcaType { get; set; }
    }

    internal abstract class Order
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

        public override bool Equals(object obj)
        {
            Debug.Assert(Id > 0);
            var other = obj as Order;
            return other != null && other.Id == Id;
        }

        public override int GetHashCode()
        {
            Debug.Assert(Id > 0);
            return Id.GetHashCode();
        }
    }

    internal class MarketOrder : Order
    {
        public MarketOrder() : base("MKT") { }
    }

    internal class MarketIfTouchedOrder : Order
    {
        public MarketIfTouchedOrder() : base("MIT") { }
        public double TouchPrice { get; set; }
    }

    internal class LimitOrder : Order
    {
        public LimitOrder() : base("LMT") { }
        public double LmtPrice { get; set; }
    }

    internal class StopOrder : Order
    {
        public StopOrder() : base("STP") { }
        public double StopPrice { get; set; }
    }

    internal class TrailingStopOrder : Order
    {
        public TrailingStopOrder() : base("TRAIL") { }
        internal double StopPrice { get; set; } = double.MinValue;
        
        public double TrailingAmount{ get; set; }
        // Takes priority over TrailingAmount if set
        public double TrailingPercent { get; set; } = double.MinValue;
    }
}
