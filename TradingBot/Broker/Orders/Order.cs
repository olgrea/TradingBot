using System.Diagnostics;

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

    public enum OrderType
    {
        MKT, 
        LMT, 
        MIT,
        STP,
        TRAIL,
    }

    public enum TimeInForce
    {
        DAY, 
        GTC, 
        IOC, 
        GTD, 
        OPG, 
        FOK, 
        DTC,
    }

    internal class RequestInfo
    {
        public int OrderId { get; set; } = -1;
        public int ClientId { get; set; }
        public int ParentId { get; set; }
        public int PermId { get; set; }
        public bool Transmit { get; set; } = true; // if false, order will be created but not transmitted
        public string? OcaGroup { get; set; }
        public OcaType OcaType { get; set; }
        public TimeInForce TimeInForce { get; set; } = TimeInForce.DAY;
    }

    // TODO : to investigate/implement : 
    // Order conditioning
    // order algos
    // "One cancels all" groups
    // These order types : Passive Relative, Auction, Pegged to Primary
    public abstract class Order
    {
        protected Order() { }
        protected Order(IBApi.Order ibo) 
        {
            Action = Enum.Parse<OrderAction>(ibo.Action);
            TotalQuantity = Convert.ToDouble(ibo.TotalQuantity);
            Info = new RequestInfo()
            {
                OrderId = ibo.OrderId,
                ClientId = ibo.ClientId,
                Transmit = ibo.Transmit,
                ParentId = ibo.ParentId,
                PermId = ibo.PermId,
                OcaGroup = ibo.OcaGroup,
                OcaType = (OcaType)ibo.OcaType,
                TimeInForce = Enum.Parse<TimeInForce>(ibo.Tif),
            };

            OrderType type = Enum.Parse<OrderType>(ibo.OrderType);
            if (OrderType != type)
                throw new ArgumentException($"IBKR order is of type {type} but needs to be {OrderType}");
        }

        internal RequestInfo Info { get; set; } = new RequestInfo();
        internal int Id
        {
            get => Info.OrderId;
            set => Info.OrderId = value;
        }

        public abstract OrderType OrderType { get; }
        public OrderAction Action { get; set; }
        public double TotalQuantity { get; set; }

        public override bool Equals(object? obj)
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

        public static explicit operator Order(IBApi.Order ibo)
        {
            switch (ibo.OrderType)
            {
                case "MKT":
                    return (MarketOrder)ibo;

                case "LMT":
                    return (LimitOrder)ibo;

                case "STP":
                    return (StopOrder)ibo;

                case "TRAIL":
                    return (TrailingStopOrder)ibo;

                case "MIT":
                    return (MarketIfTouchedOrder)ibo;

                default:
                    throw new NotImplementedException($"{ibo.OrderType}");
            }
        }

        public static explicit operator IBApi.Order(Order order)
        {
            return new IBApi.Order()
            {
                OrderType = order.OrderType.ToString(),
                Action = order.Action.ToString(),
                TotalQuantity = Convert.ToDecimal(order.TotalQuantity),

                OrderId = order.Id,
                ClientId = order.Info.ClientId,
                ParentId = order.Info.ParentId,
                PermId = order.Info.PermId,
                Transmit = order.Info.Transmit,
                OcaGroup = order.Info.OcaGroup,
                OcaType = (int)order.Info.OcaType,

                OutsideRth = false,
                Tif = order.Info.TimeInForce.ToString(),
            };
        }
    }

    public class MarketOrder : Order
    {
        public MarketOrder() { }
        public MarketOrder(IBApi.Order ibo) : base(ibo) {}

        public override OrderType OrderType => OrderType.MKT;

        public static explicit operator IBApi.Order(MarketOrder order) => (IBApi.Order)(order as Order);

        public static explicit operator MarketOrder(IBApi.Order ibo) => new MarketOrder(ibo);
    }

    public class MarketIfTouchedOrder : Order
    {
        public MarketIfTouchedOrder() { }
        public MarketIfTouchedOrder(IBApi.Order ibo) : base(ibo) 
        {
            TouchPrice = ibo.AuxPrice;
        }

        public double TouchPrice { get; set; }

        public override OrderType OrderType => OrderType.MIT;

        public override string ToString()
        {
            return $"{base.ToString()} Touch price : {TouchPrice:c}";
        }

        public static explicit operator IBApi.Order(MarketIfTouchedOrder order)
        {
            var ibo = (IBApi.Order)(order as Order);
            ibo.AuxPrice = order.TouchPrice;
            return ibo;
        }

        public static explicit operator MarketIfTouchedOrder(IBApi.Order ibo) => new MarketIfTouchedOrder(ibo);
    }

    public class LimitOrder : Order
    {
        public LimitOrder() { }
        public LimitOrder(IBApi.Order ibo) : base(ibo) 
        {
            LmtPrice = ibo.LmtPrice;
        }

        public double LmtPrice { get; set; }

        public override OrderType OrderType => OrderType.LMT;

        public override string ToString()
        {
            return $"{base.ToString()} Limit price : {LmtPrice:c}";
        }

        public static explicit operator IBApi.Order(LimitOrder order)
        {
            var ibo = (IBApi.Order)(order as Order);
            ibo.LmtPrice = order.LmtPrice;
            return ibo;
        }

        public static explicit operator LimitOrder(IBApi.Order ibo) => new LimitOrder(ibo);
    }

    public class StopOrder : Order
    {
        public StopOrder() { }
        public StopOrder(IBApi.Order ibo) : base(ibo) 
        {
            StopPrice = ibo.AuxPrice;
        }
        public double StopPrice { get; set; }

        public override OrderType OrderType => OrderType.STP;

        public override string ToString()
        {
            return $"{base.ToString()} Stop price : {StopPrice:c}";
        }

        public static explicit operator IBApi.Order(StopOrder order)
        {
            var ibo = (IBApi.Order)(order as Order);
            ibo.AuxPrice = order.StopPrice;
            return ibo;
        }

        public static explicit operator StopOrder(IBApi.Order ibo) => new StopOrder(ibo);
    }

    public enum TrailingAmountUnits
    {
        Absolute,
        Percent
    }

    public class TrailingStopOrder : Order
    {
        double? _trailingAmount;
        TrailingAmountUnits? _units;

        public TrailingStopOrder() { }
        public TrailingStopOrder(IBApi.Order ibo) : base(ibo)
        {
            StopPrice = ibo.TrailStopPrice;

            // TrailingPercent takes precedence apparently. See comments of IBApi.Order.TrailingPercent 
            if (ibo.TrailingPercent != double.MaxValue)
            {
                TrailingAmount = ibo.TrailingPercent;
                TrailingAmountUnits = Orders.TrailingAmountUnits.Percent;
            }
            else if (ibo.AuxPrice != double.MaxValue)
            {
                TrailingAmount = ibo.AuxPrice;
                TrailingAmountUnits = Orders.TrailingAmountUnits.Absolute;
            }
            else
                throw new ArgumentException($"Neither Trailing percent or trailing amount is set in IBKR order");
        }
        
        public double? StopPrice { get; set; }
        public double? TrailingAmount
        {
            get => _trailingAmount;
            set
            {
                if (value is not null && _units == Orders.TrailingAmountUnits.Percent)
                    value = Math.Clamp(value.Value, 0.0, 1.0);

                _trailingAmount = value;
            }
        }

        public TrailingAmountUnits? TrailingAmountUnits
        {
            get => _units;
            set
            {
                if (value is not null && value == Orders.TrailingAmountUnits.Percent && _trailingAmount is not null)
                    _trailingAmount = Math.Clamp(_trailingAmount.Value, 0.0, 1.0);

                _units = value;
            }
        }

        public override OrderType OrderType => OrderType.TRAIL;

        public override string ToString()
        {
            if (_units == Orders.TrailingAmountUnits.Percent)
                return $"{base.ToString()} TrailingAmount : {_trailingAmount * 100} %";
            else if (_units == Orders.TrailingAmountUnits.Absolute)
                return $"{base.ToString()} TrailingAmount : {TrailingAmount:c}";
            else
                return base.ToString();
        }

        public static explicit operator IBApi.Order(TrailingStopOrder order)
        {
            var ibo = (IBApi.Order)(order as Order);
            ibo.TrailStopPrice = order.StopPrice is null ? double.MaxValue : order.StopPrice.Value;

            ArgumentNullException.ThrowIfNull(order.TrailingAmount, nameof(order.TrailingAmount));
            ArgumentNullException.ThrowIfNull(order.TrailingAmountUnits, nameof(order.TrailingAmountUnits));

            if (order.TrailingAmountUnits == Orders.TrailingAmountUnits.Absolute)
                ibo.AuxPrice = order.TrailingAmount.Value;
            else if (order.TrailingAmountUnits == Orders.TrailingAmountUnits.Percent)
                ibo.TrailingPercent = order.TrailingAmount.Value;
            else
                throw new NotImplementedException($"{order.TrailingAmountUnits}");

            return ibo;
        }

        public static explicit operator TrailingStopOrder(IBApi.Order ibo) => new TrailingStopOrder(ibo);
    }
}
