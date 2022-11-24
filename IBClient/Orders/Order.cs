using System.Diagnostics;

namespace InteractiveBrokers.Orders
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
        public bool Transmit { get; set; } = true; // if false, order will be created but not transmitted
        public string OcaGroup { get; set; }
        public OcaType OcaType { get; set; }
    }

    // TODO : implement order conditioning
    // https://interactivebrokers.github.io/tws-api/order_conditions.html
    public abstract class Order
    {
        public RequestInfo Info { get; } = new RequestInfo();
        public int Id
        {
            get => Info.OrderId;
            set => Info.OrderId = value;
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

        public override string ToString()
        {
            return $"[{Id}] : {Action} {TotalQuantity} {OrderType}";
        }
    }

    public class MarketOrder : Order
    {
        public MarketOrder() : base("MKT") { }
    }

    public class MarketIfTouchedOrder : Order
    {
        public MarketIfTouchedOrder() : base("MIT") { }
        public double TouchPrice { get; set; }

        public override string ToString()
        {
            return $"{base.ToString()} Touch price : {TouchPrice:c}";
        }
    }

    public class LimitOrder : Order
    {
        public LimitOrder() : base("LMT") { }
        public double LmtPrice { get; set; }

        public override string ToString()
        {
            return $"{base.ToString()} Limit price : {LmtPrice:c}";
        }
    }

    public class StopOrder : Order
    {
        public StopOrder() : base("STP") { }
        public double StopPrice { get; set; }

        public override string ToString()
        {
            return $"{base.ToString()} Stop price : {StopPrice:c}";
        }
    }

    public class TrailingStopOrder : Order
    {
        public TrailingStopOrder() : base("TRAIL") { }
        public double StopPrice { get; set; } = double.MaxValue;

        public double TrailingAmount { get; set; } = double.MaxValue;
        // Takes priority over TrailingAmount if set
        public double TrailingPercent { get; set; } = double.MaxValue;

        public override string ToString()
        {
            if (TrailingPercent != double.MaxValue)
                return $"{base.ToString()} TrailingPercent : {TrailingPercent}";
            else if (TrailingAmount != double.MaxValue)
                return $"{base.ToString()} TrailingAmount : {TrailingAmount:c}";
            else
                return base.ToString();
        }
    }
}
